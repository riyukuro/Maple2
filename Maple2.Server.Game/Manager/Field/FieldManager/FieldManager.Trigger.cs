using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    public readonly TriggerCollection TriggerObjects;

    private readonly ConcurrentDictionary<int, FieldBreakable> triggerBreakable = new();
    private readonly ConcurrentDictionary<string, FieldTrigger> fieldTriggers = new();
    public readonly Dictionary<string, TickTimer> Timers = new();
    public readonly TriggerUserValues UserValues = new();
    public readonly Dictionary<string, Widget> Widgets = new();
    public readonly Dictionary<int, List<TriggerState>> States = new();
    // The score the score_board_score condition tests: score_board_set_score sets it, and while a board is open
    // AddScoreBoardScore adds kill and orb points to it; null until either, and score_board_create/remove clear it.
    public int? ScoreBoardScore;
    private readonly object scoreBoardLock = new();
    // The 0xE1 board open in the field (score_board_create or shadow_expedition_open_boss_gauge; null when none
    // is open): set_score updates its points, a player who enters gets it, remove/close clears it.
    public ScoreBoard.Board? ScoreBoardOpen;
    // The kill count behind the open boss gauge of shadow_expedition_open_boss_gauge; null when none is open.
    public ShadowGauge? ShadowGauge;
    // Dungeon variables of a field that is not part of a dungeon instance (a dungeon's fields use their room
    // record's store instead).
    public readonly ConcurrentDictionary<int, int> DungeonVariables = new();
    // Nif effects add_effect_nif put on npcs, by npc object id; a player who enters later gets them in one
    // Trigger LoadEffects packet, as the client keeps them in its own table by object id.
    public readonly ConcurrentDictionary<int, (string NifPath, bool IsOutline, float Scale, float RotateZ)> NpcEffects = new();

    // The score orb (item 90000019) the bonus maps' spawn_item_range drops: its amount goes to the open board.
    public const int ScoreBoardItemId = 90000019;

    // Points for the open board (chosen here; the scripts give no other source): the score of a spawn_npc_range
    // npc that dies, and the amount of a picked-up score orb. 80000014_bonus checks score_board_score >= 18000
    // against 8,300 (83 npcs x 100) + 1,500 (its 2001 npc) + 10 x 800 (the orbs) = 17,800. The client shows the
    // points on the 0xE1 board (FieldGameScoreBoardCli). Nothing is added with no board open (02000385_bf,
    // 02000393_bf and 99999908 score kills but open none); false then.
    public bool AddScoreBoardScore(int points) {
        if (points <= 0) {
            return false;
        }

        int total;
        lock (scoreBoardLock) {
            if (ScoreBoardOpen == null) {
                return false;
            }
            total = (int) Math.Min((long) (ScoreBoardScore ?? 0) + points, int.MaxValue);
            ScoreBoardScore = total;
            ScoreBoardOpen = ScoreBoardOpen with { Points = total };
        }
        Broadcast(ShadowExpeditionPacket.SetPoints(total));
        return true;
    }

    // shadow_expedition_open_boss_gauge: the 0xE1 boss gauge at 0 points, counting the monsters alive now (not a
    // player's summon; one a destroy_monster of the same tick still holds leaves the count when it goes).
    public void OpenShadowGauge(string title, int maxPoints) {
        ScoreBoard.Board board;
        lock (scoreBoardLock) {
            ShadowGauge = new ShadowGauge(ShadowGauge.AtOpen(Mobs.Values.Select(mob => (mob.ObjectId, mob.IsDead, mob.PlayerSummon))), maxPoints);
            ScoreBoardScore = 0;
            board = ScoreBoardOpen = new ScoreBoard.Board(ShadowExpeditionPacket.BossGauge, title, 0, maxPoints);
        }
        Broadcast(ScoreBoard.OpenPacket(board));
    }

    // Whether one of the field's trigger scripts opens a boss gauge, read once.
    public bool OpensShadowGauge => opensShadowGauge ??= TriggerCache != null && Entities.TriggerModels.Values.Any(model =>
        TriggerCache.TryGet(Metadata.XBlock, model.Name.ToLower(), out Trigger.Helpers.Trigger? trigger) && trigger.OpensShadowGauge());
    private bool? opensShadowGauge;

    // Closes the open board (0xE1 command 2) and stops the gauge's count.
    public void CloseScoreBoard() {
        lock (scoreBoardLock) {
            ShadowGauge = null;
            ScoreBoardOpen = null;
        }
        Broadcast(ShadowExpeditionPacket.Close());
    }

    // A monster's death or removal while a boss gauge is open: the gauge's new points go to the board.
    public void ShadowGaugeKilled(int objectId) => SetShadowGaugePoints(ShadowGauge?.Killed(objectId));

    public void ShadowGaugeRemoved(int objectId) => SetShadowGaugePoints(ShadowGauge?.Removed(objectId));

    private void SetShadowGaugePoints(int? points) {
        if (points == null) {
            return;
        }
        lock (scoreBoardLock) {
            if (ScoreBoardOpen == null || ShadowGauge == null) {
                return;
            }
            ScoreBoardScore = points;
            ScoreBoardOpen = ScoreBoardOpen with { Points = points.Value };
        }
        Broadcast(ShadowExpeditionPacket.SetPoints(points.Value));
    }

    // A picked-up score orb gives no item, board or not: true for it (its amount added to an open board).
    public bool TakeScoreOrb(Item item) {
        if (item.Id != ScoreBoardItemId) {
            return false;
        }
        AddScoreBoardScore(item.Amount);
        return true;
    }

    // The effects of npcs still in the field, in the LoadEffects packet's form.
    public static List<(int ObjectId, string NifPath, bool IsOutline, float Scale, float RotateZ)> LiveNpcEffects(
            IReadOnlyDictionary<int, (string NifPath, bool IsOutline, float Scale, float RotateZ)> effects, Func<int, bool> isLive) {
        return effects.Where(entry => isLive(entry.Key))
            .OrderBy(entry => entry.Key)
            .Select(entry => (entry.Key, entry.Value.NifPath, entry.Value.IsOutline, entry.Value.Scale, entry.Value.RotateZ))
            .ToList();
    }

    // The ceremony of a wedding hall room, set when the first character enters it by Wedding 0x1a; null in every
    // other field.
    public WeddingCeremony? Wedding;

    public FieldTrigger? AddTrigger(TriggerModel trigger) {
        try {
            var fieldTrigger = new FieldTrigger(this, NextLocalId(), trigger) {
                Position = trigger.Position,
                Rotation = trigger.Rotation,
            };
            fieldTriggers[trigger.Name] = fieldTrigger;

            return fieldTrigger;
        } catch (ArgumentException ex) {
            logger.Warning(ex, "Invalid Trigger: {Exception}", ex.Message);
            return null;
        }
    }

    public ICollection<FieldTrigger> EnumerateTrigger() => fieldTriggers.Values;

    private int fieldWarCountdown;

    // A field war room (map type FieldWar) takes players for FieldWarInstanceEnterableDurationSeconds from its first
    // one; then its triggers get FieldGameStart (FieldWarRules.StartValue): 1 starts the war, 2 closes the room.
    private void StartFieldWarCountdown() {
        if (Metadata.Property.Type != MapType.FieldWar || Interlocked.Exchange(ref fieldWarCountdown, 1) != 0) {
            return;
        }
        Scheduler.Schedule(() => {
            int value = FieldWarRules.StartValue(Players.Count, Constants.FieldWarRequirePlayerCount);
            foreach (FieldTrigger trigger in fieldTriggers.Values) {
                UserValues.Set(trigger.Value.Id, FieldWarRules.StartKey, value);
            }
        }, TimeSpan.FromSeconds(Constants.FieldWarInstanceEnterableDurationSeconds));
    }

    private int fieldWarEnded;

    // FieldWarEnd: the room's war ends once (a later FieldWarEnd does nothing); a clear gives each player here the
    // war's reward content (FieldWarRules.ClearReward). Returns the reward given, 0 for none.
    public int EndFieldWar(bool isClear) {
        if (Interlocked.Exchange(ref fieldWarEnded, 1) != 0) {
            return 0;
        }
        IEnumerable<FieldWarMetadata> wars = ServerTableMetadata.TimeEventTable.FieldWar?.Values ?? [];
        int rewardId = FieldWarRules.ClearReward(wars, MapId, isClear);
        if (rewardId != 0) {
            foreach (FieldPlayer player in Players.Values) {
                player.Session.GetRewardContent(rewardId);
            }
        }
        return rewardId;
    }

    public bool TryGetTrigger(string name, [NotNullWhen(true)] out FieldTrigger? fieldTrigger) {
        return fieldTriggers.TryGetValue(name, out fieldTrigger);
    }

    // Used for debugging triggers
    public bool ReplaceTrigger(FieldTrigger oldFieldTrigger, FieldTrigger newFieldTrigger) {
        return fieldTriggers.TryUpdate(oldFieldTrigger.Value.Name, newFieldTrigger, oldFieldTrigger);
    }

    // The timeEventData type 6 rows of the map (FORK.md "Field trigger events"): at each run start the row's
    // trigger gets its key = value and TimeEventLifeTime. The field reads them at its first update (a run alive then
    // is given for what is left of it) and they stop with the field. Dungeon and home fields take none.
    private sealed class TriggerEventState {
        public required FieldTriggerEventMetadata Row { get; init; }
        public DateTime? NextRun;
    }

    private List<TriggerEventState>? triggerEvents;

    private List<TriggerEventState> TriggerEventStates() {
        if (triggerEvents != null) {
            return triggerEvents;
        }
        triggerEvents = [];
        if (this is DungeonFieldManager or HomeFieldManager) {
            return triggerEvents;
        }
        IEnumerable<FieldTriggerEventMetadata> rows = ServerTableMetadata.TimeEventTable.FieldTriggerEvent?.Values ?? [];
        foreach (FieldTriggerEventMetadata row in rows.Where(row => row.MapIds.Contains(MapId)).OrderBy(row => row.Id)) {
            triggerEvents.Add(new TriggerEventState { Row = row });
        }
        return triggerEvents;
    }

    internal void UpdateTriggerEvents(DateTime now) {
        foreach (TriggerEventState state in TriggerEventStates()) {
            if (state.NextRun == null) {
                if (FieldTriggerEventRules.CurrentRun(state.Row, now) is { } current) {
                    RunTriggerEvent(state.Row, now, current + state.Row.LifeTime);
                }
                state.NextRun = FieldTriggerEventRules.NextRun(state.Row, now, Random.Shared.NextDouble()) ?? DateTime.MaxValue;
                continue;
            }
            if (now < state.NextRun) {
                continue;
            }
            RunTriggerEvent(state.Row, now, now + state.Row.LifeTime);
            state.NextRun = FieldTriggerEventRules.NextRun(state.Row, now, Random.Shared.NextDouble()) ?? DateTime.MaxValue;
        }
    }

    private void RunTriggerEvent(FieldTriggerEventMetadata row, DateTime now, DateTime ends) {
        int seconds = FieldTriggerEventRules.SecondsLeft(now, ends);
        if (seconds <= 0 || !FieldInteractEventRules.Runs(row.Probability, Random.Shared.Next(100))) {
            return;
        }
        FieldTriggerEventRules.Apply(row, UserValues, seconds);
    }
}

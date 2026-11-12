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

    public bool TryGetTrigger(string name, [NotNullWhen(true)] out FieldTrigger? fieldTrigger) {
        return fieldTriggers.TryGetValue(name, out fieldTrigger);
    }

    // Used for debugging triggers
    public bool ReplaceTrigger(FieldTrigger oldFieldTrigger, FieldTrigger newFieldTrigger) {
        return fieldTriggers.TryUpdate(oldFieldTrigger.Value.Name, newFieldTrigger, oldFieldTrigger);
    }
}

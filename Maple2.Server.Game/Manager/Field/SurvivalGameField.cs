using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// A Maple Survival game in a survival map's field (FieldManager.Survival): started by the script's
// CreateFieldGame MapleSurvival. The participants fight each other (the map's PK mode makes them the client's enemies);
// a fallen one gets its finish (Survival 0x14) and result, the others the kill feed (0x19) and the alive count (0x1a);
// the last one standing wins and every participant here gets the result (0x11) and the game's end (0x14 true). The
// storm (SurvivalContentsWidget) hurts a living participant outside the safe zone. See SurvivalGameState for the rules.
public sealed class SurvivalGameField {
    // Chosen here: the storm takes StormDamageRate of a participant's total health every StormTickMs, with the
    // "outside the safe zone" message (0x1d) every StormNoticeMs.
    public const long StormTickMs = 1_000;
    public const long StormNoticeMs = 5_000;
    public const float StormDamageRate = 0.05f;

    private readonly ILogger logger = Log.Logger.ForContext<SurvivalGameField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly SurvivalGameState state;
    private readonly Dictionary<long, string> names = [];
    private readonly long startTick;
    // The participants whose result (0x11) went out: once each, when its squad's rank is known.
    private readonly HashSet<long> resulted = [];
    private long nextStormTick;
    private long nextNoticeTick;

    public bool Ended {
        get {
            lock (mutex) return state.Ended;
        }
    }

    public SurvivalGameField(FieldManager field, IEnumerable<FieldPlayer> players) {
        this.field = field;
        startTick = field.FieldTick;
        List<FieldPlayer> list = players.ToList();
        foreach (FieldPlayer player in list) {
            names[player.Value.Character.Id] = player.Value.Character.Name;
        }
        // A squad match's field knows each character's squad (ArenaMatchField.Units); squad members are no enemies.
        ArenaMatchField? arena = field.Arena;
        state = new SurvivalGameState(list.Select(player => player.Value.Character.Id), arena != null ? arena.UnitOf : null);
        field.Broadcast(MapleSurvivalPacket.GameStart());
        field.Broadcast(MapleSurvivalPacket.AliveCount(state.Alive));
        logger.Information("Maple Survival game on {MapId} started with {Count} players", field.MapId, list.Count);
        if (state.Ended) {
            Finish();
            RunAfter();
        }
    }

    // Whom a participant that fell may watch: the character it asks for when that one is still playing, else the
    // first one still playing; 0 while it plays itself or when nobody does.
    public long WatchTarget(long requester, long wanted) {
        lock (mutex) {
            if (state.Get(requester) == null || state.IsAlive(requester)) {
                return 0;
            }
            if (state.IsAlive(wanted)) {
                return wanted;
            }
            return state.Entries.FirstOrDefault(entry => state.IsAlive(entry.CharacterId))?.CharacterId ?? 0;
        }
    }

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
    }

    public void Damage(FieldPlayer attacker, FieldPlayer victim) {
        lock (mutex) state.Hit(attacker.Value.Character.Id, victim.Value.Character.Id, field.FieldTick);
    }

    public void Death(FieldPlayer victim) {
        lock (mutex) Fall(victim.Value.Character.Id);
        RunAfter();
    }

    public void Leave(FieldPlayer player) {
        lock (mutex) Fall(player.Value.Character.Id);
        RunAfter();
    }

    private void Fall(long characterId) {
        if (!state.Fall(characterId, field.FieldTick, out long killer)) {
            return;
        }
        if (killer != 0) {
            field.Broadcast(MapleSurvivalPacket.KillFeed(names.GetValueOrDefault(killer, string.Empty), names.GetValueOrDefault(characterId, string.Empty), 0, state.Alive));
        }
        field.Broadcast(MapleSurvivalPacket.AliveCount(state.Alive));
        if (field.TryGetPlayerById(characterId, out FieldPlayer? fallen)) {
            fallen.Session.Send(MapleSurvivalPacket.GameFinish(false, [characterId]));
        }
        // The squad's last member fell: every member gets the squad's rank (a solo player at once).
        SendResults(state.UnitOf(characterId).Where(member => member.Rank != 0 && !state.Ended));
        if (state.Ended) {
            Finish();
        }
    }

    // The storm, from the field's update.
    public void Update(long tick) {
        lock (mutex) {
            if (state.Ended || tick < nextStormTick) {
                return;
            }
            nextStormTick = tick + StormTickMs;
            if (field.Widgets.GetValueOrDefault("SurvivalContents") is not SurvivalContentsWidget { Storm: { Step: > 0 } storm } widget) {
                return;
            }
            bool notice = tick >= nextNoticeTick;
            foreach (FieldPlayer player in field.Players.Values) {
                if (!state.IsAlive(player.Value.Character.Id) || player.IsDead) {
                    continue;
                }
                Vector3 offset = player.Position - widget.Center;
                if (!SurvivalStorm.Outside(offset.X, offset.Y, storm.SafeRadius)) {
                    continue;
                }
                player.ConsumeHp((int) Math.Max(1, player.Stats.Values[BasicAttribute.Health].Total * StormDamageRate));
                if (notice) {
                    player.Session.Send(MapleSurvivalPacket.InStorm());
                }
            }
            if (notice) {
                nextNoticeTick = tick + StormNoticeMs;
            }
        }
    }

    // The game result (0x11, the restored result window): the rating, kills, rank, survival level and exp and the
    // seconds played (MapleSurvivalProgress.Result, SurvivalRules.ResultValues); no items.
    // The participants are counted in squads (a solo player is one), as the ranks are.
    // The work a fall or the end leaves for after the lock: the results' saves and the World's end call.
    private readonly List<Action> after = [];

    private void RunAfter() {
        List<Action> work;
        lock (mutex) {
            work = after.ToList();
            after.Clear();
        }
        foreach (Action action in work) {
            try {
                action();
            } catch (Exception ex) {
                logger.Warning(ex, "Maple Survival result not completed on {MapId}", field.MapId);
            }
        }
    }

    private void SendResults(IEnumerable<SurvivalGameState.Entry> entries) {
        int squads = state.Entries.Select(entry => entry.Unit).Distinct().Count();
        int seconds = (int) Math.Max(0, (field.FieldTick - startTick) / 1000);
        foreach (SurvivalGameState.Entry entry in entries.ToList()) {
            if (!resulted.Add(entry.CharacterId) || !field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                continue;
            }
            bool squad = state.UnitOf(entry.CharacterId).Count() > 1;
            int rank = entry.Rank;
            int kills = entry.Kills;
            // The save and the lobby resend run after the field's lock (RunAfter): a slow save would stall the hits.
            after.Add(() => {
                int[] values = MapleSurvivalProgress.Result(player.Session, rank, squads, kills, seconds, squad);
                player.Session.Send(MapleSurvivalPacket.Result(values));
            });
        }
    }

    // The game's end: every participant is finished; the results not sent yet (the winning squad, and a squad whose
    // last fall ended the game) go out; the arena ranking has the winning squad first.
    private void Finish() {
        List<long> winners = state.Winner != 0 ? state.UnitOf(state.Winner).Select(entry => entry.CharacterId).ToList() : [];
        var finished = state.Fallen.Where(id => !winners.Contains(id)).Concat(winners).ToList();
        SendResults(state.Entries.Where(entry => entry.Rank != 0));
        field.Broadcast(MapleSurvivalPacket.GameFinish(true, finished));
        (field.Widgets.GetValueOrDefault("SurvivalContents") as SurvivalContentsWidget)?.Stop();
        logger.Information("Maple Survival game on {MapId} ended, winner {Winner} ({Count} in the squad)", field.MapId, state.Winner, winners.Count);
        var ranking = new List<long>(winners);
        ranking.AddRange(state.Fallen.Reverse().Where(id => !winners.Contains(id)));
        ArenaMatchField? arena = field.Arena;
        after.Add(() => arena?.ZoneEnded(ranking, winners.Count));
    }
}

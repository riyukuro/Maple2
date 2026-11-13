using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// The Red Arena's PvP zone (set_pvp_zone type 1): face-offs of two participants at a time (RedArenaState). The steps
// are announced to the field with the client's PvP strings (MassiveEvent text of kind 3: s_dualprepareplayer 10,
// s_dualstartcombat 11, s_dualwin1 to 9); the two fighters get their own PvP points in mode 1 for their fight
// (Pvp 0x64; the client's enemies). At the end the champion gets the win pose and redDuelWin exp, the others the loss
// pose and redDuelLose exp, and the arena match (if any) gives its row's reward, the champion first.
public sealed class RedArenaZoneField : IPvpZoneField {
    private const byte Mode = 1;
    private const int TextDurationMs = 3_000;
    private const int PrepareText = 10;
    private const int StartText = 11;

    private readonly ILogger logger = Log.Logger.ForContext<RedArenaZoneField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly RedArenaState state;
    private readonly int additionalEffectId;
    private readonly Func<IEnumerable<FieldPlayer>> playersInBox;
    private readonly Action<IReadOnlyList<long>, int>? onEnd;
    private readonly (Vector3 Position, Vector3 Rotation)? stage;
    // Chosen here: the two fighters stand StageOffset either side of the stage's spot.
    private const float StageOffset = 150f;

    public int BoxId => state.BoxId;
    // The zone's end as the trigger sees it (pvp_zone_ended). 65000001's enter.xml then sends WINNER (event ui type 3)
    // to box 102, the stage, and later Bonus (type 6) to everyone off it; the box test is by position only. A fighter
    // beaten by a death lies on the stage until the map's auto revival (autoRevivalTime 3) moves it to the player spawn
    // on box 101, so the end waits until no beaten participant in the field is dead, at most EndHoldMs (chosen here).
    // Before, the last loser got the champion's WINNER.
    public bool Ended {
        get {
            lock (mutex) return state.Ended && !Holding();
        }
    }

    private const long EndHoldMs = 10_000;
    private long endTick;

    private bool Holding() {
        if (field.FieldTick >= endTick + EndHoldMs) {
            return false;
        }
        return field.Players.Values.Any(player => player.IsDead && player.Value.Character.Id != state.Champion
                                                  && state.IsParticipant(player.Value.Character.Id));
    }

    public RedArenaZoneField(FieldManager field, int boxId, int prepareSeconds, int matchSeconds, int additionalEffectId,
                             Func<IEnumerable<FieldPlayer>> playersInBox, Action<IReadOnlyList<long>, int>? onEnd, long tick, Random? random = null,
                             (Vector3 Position, Vector3 Rotation)? stage = null) {
        this.field = field;
        this.stage = stage;
        this.additionalEffectId = additionalEffectId;
        this.playersInBox = playersInBox;
        this.onEnd = onEnd;
        state = new RedArenaState(boxId, tick, prepareSeconds, matchSeconds, random);
    }

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
    }

    public bool IsParticipant(FieldPlayer player) {
        lock (mutex) return state.Active && !state.Ended && state.IsParticipant(player.Value.Character.Id);
    }

    public void Update(long tick) {
        lock (mutex) {
            if (state.DueToActivate(tick)) {
                Apply(state.Activate(playersInBox().Select(player => player.Value.Character.Id).ToList(), tick));
                foreach (FieldPlayer player in field.Players.Values.Where(player => state.IsParticipant(player.Value.Character.Id))) {
                    PvpConditions.Participation(player, field.MapId);
                }
                return;
            }
            Apply(state.Update(tick));
        }
    }

    private readonly KillCredit killCredit = new();

    // A hit between the face-off's fighters, for the kill credit.
    public void Damage(FieldPlayer attacker, FieldPlayer victim, long amount) {
        lock (mutex) {
            if (state.Hostile(attacker.Value.Character.Id, victim.Value.Character.Id)) {
                killCredit.Hit(attacker.Value.Character.Id, victim.Value.Character.Id, field.FieldTick);
            }
        }
    }

    // A fighter that falls in a face-off loses it: pvp_die for it, and pvp_kill for the other fighter, who wins the
    // face-off by that death, when it hit the victim within PvpZoneState.KillCreditMs (a fall is no kill; a fighter
    // that leaves gives none).
    public void Death(FieldPlayer victim) {
        lock (mutex) {
            long victimId = victim.Value.Character.Id;
            bool fighting = state.Fighting;
            long killerId = killCredit.Killer(victimId, field.FieldTick);
            List<RedArenaEvent> events = state.Out(victimId, field.FieldTick);
            if (fighting && events.FirstOrDefault(@event => @event.Type == RedArenaEventType.Result) is { Winner: not 0 } result) {
                PvpConditions.Death(victim, field.MapId);
                if (killerId == result.Winner && field.TryGetPlayerById(killerId, out FieldPlayer? killer)) {
                    PvpConditions.Kill(killer, field.MapId);
                }
            }
            Apply(events);
        }
    }

    public void Enter(FieldPlayer player) { }

    public void Leave(FieldPlayer player) {
        lock (mutex) Apply(state.Out(player.Value.Character.Id, field.FieldTick));
    }

    private void Apply(List<RedArenaEvent> events) {
        foreach (RedArenaEvent @event in events) {
            switch (@event.Type) {
                case RedArenaEventType.Prepare:
                    field.Broadcast(MassiveEventPacket.Text(InterfaceText.Pvp(PrepareText), TextDurationMs));
                    ToStage(@event.First, -StageOffset);
                    ToStage(@event.Second, StageOffset);
                    break;
                case RedArenaEventType.Start:
                    field.Broadcast(MassiveEventPacket.Text(InterfaceText.Pvp(StartText), TextDurationMs));
                    int point = PvpPoints.First;
                    foreach (long characterId in new[] { @event.First, @event.Second }) {
                        if (field.TryGetPlayerById(characterId, out FieldPlayer? fighter)) {
                            field.Broadcast(PvpPacket.Point(fighter.ObjectId, point, 0, Mode));
                            if (additionalEffectId > 0) {
                                fighter.Buffs.AddBuff(field.FieldActor, fighter, additionalEffectId, 1, field.FieldTick);
                            }
                        }
                        point++;
                    }
                    ShowFight(@event.First, @event.Second);
                    break;
                case RedArenaEventType.Result:
                    SendToSpectators(@event.First, @event.Second,
                        TriggerPacket.PvpObserverDialog(0, default, default));
                    foreach (long characterId in new[] { @event.First, @event.Second }) {
                        if (field.TryGetPlayerById(characterId, out FieldPlayer? fighter)) {
                            field.Broadcast(PvpPacket.Point(fighter.ObjectId, 0, 0, 0));
                        }
                    }
                    // A face-off the time ended (no winner): both fighters leave the stage for a player spawn. A fighter
                    // that fell revives there anyway; one that left is gone.
                    foreach (long characterId in @event.Winner == 0 ? new[] { @event.First, @event.Second } : []) {
                        if (field.TryGetPlayerById(characterId, out FieldPlayer? loser) && !loser.IsDead
                            && field.TryGetPlayerSpawn(-1, out FieldPlayerSpawnPoint? spawn)) {
                            loser.MoveToPosition(spawn.Position, spawn.Rotation);
                        }
                    }
                    if (@event.Winner != 0 && field.TryGetPlayerById(@event.Winner, out FieldPlayer? winner)) {
                        // s_dualwin1 to 8 by the win count; 9 is kept for the end with no challenger left.
                        field.Broadcast(MassiveEventPacket.Text(InterfaceText.Pvp(Math.Min(@event.Wins, 8), winner.Value.Character.Name), TextDurationMs));
                    }
                    break;
                case RedArenaEventType.End:
                    End(@event);
                    break;
            }
        }
    }

    // A picked fighter goes onto the stage (MoveByPortal), offset along X; the champion is moved too, so it faces the
    // challenger from its own side.
    private void ToStage(long characterId, float offset) {
        if (stage is not { } spot || !field.TryGetPlayerById(characterId, out FieldPlayer? fighter)) {
            return;
        }
        // Sent as is: MoveToPosition drops a spot the navmesh does not find, and 65000001's stage portal lies where the
        // nearest-poly search finds nothing (no move and no log in a live run).
        fighter.Session.Send(PortalPacket.MoveByPortal(fighter, field.StageSpot(spot.Position + new Vector3(offset, 0, 0)), spot.Rotation));
    }

    // The observer dialog (Trigger dialog kind 2) for the field's other players: both fighters' health and wins (the
    // champion's win count); the client follows their health itself (PC::vfunc_74 for players with a PvP point).
    // Chosen here: shown for the face-off's longest time.
    private void ShowFight(long first, long second) {
        if (!field.TryGetPlayerById(first, out FieldPlayer? a) || !field.TryGetPlayerById(second, out FieldPlayer? b)) {
            return;
        }
        SendToSpectators(first, second, TriggerPacket.PvpObserverDialog((int) RedArenaState.FightMs, Fighter(a), Fighter(b)));
    }

    private TriggerPacket.PvpFighter Fighter(FieldPlayer player) {
        long characterId = player.Value.Character.Id;
        byte wins = (byte) Math.Min(characterId == state.Champion ? state.ChampionWins : 0, byte.MaxValue);
        return new TriggerPacket.PvpFighter(characterId, (int) player.Stats.Values[BasicAttribute.Health].Current,
            (int) player.Stats.Values[BasicAttribute.Health].Total, wins);
    }

    private void SendToSpectators(long first, long second, Maple2.PacketLib.Tools.ByteWriter packet) {
        foreach (FieldPlayer player in field.Players.Values) {
            long characterId = player.Value.Character.Id;
            if (characterId != first && characterId != second) {
                player.Session.Send(packet);
            }
        }
    }

    private void End(RedArenaEvent @event) {
        endTick = field.FieldTick;
        long champion = @event.Winner;
        if (champion != 0 && field.TryGetPlayerById(champion, out FieldPlayer? hero)) {
            field.Broadcast(MassiveEventPacket.Text(InterfaceText.Pvp(9, hero.Value.Character.Name), TextDurationMs));
        }
        var ranking = new List<long>();
        if (champion != 0) {
            ranking.Add(champion);
        }
        foreach (FieldPlayer player in field.Players.Values) {
            long characterId = player.Value.Character.Id;
            if (!state.IsParticipant(characterId)) {
                continue;
            }
            bool won = characterId == champion;
            player.Session.Send(PvpPacket.WinLose(won));
            if (won) {
                PvpConditions.Win(player, field.MapId);
            }
            if (field.Arena != null) {
                ExpType expType = won ? ExpType.redDuelWin : ExpType.redDuelLose;
                long exp = player.Session.Exp.AddExp(expType);
                logger.Information("Red Arena zone {BoxId}: {CharacterId} got {Exp} {ExpType} exp", state.BoxId, characterId, exp, expType);
            }
            if (!won) {
                ranking.Add(characterId);
            }
        }
        logger.Information("Red Arena zone {BoxId} on {MapId} ended, champion {Champion}", state.BoxId, field.MapId, champion);
        onEnd?.Invoke(ranking, champion != 0 ? 1 : 0);
    }
}

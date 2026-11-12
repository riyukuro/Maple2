using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// Treasure Island's PvP zone (set_pvp_zone type 3): two teams (TeamZoneState). When the fight starts each
// participant gets its team's PvP point in mode 3 (Pvp 0x64), its row (0x6d), the score (0x6c), the gauge (0x6f)
// and the team result dialog's reset (0x6b); flags and plunders are announced (0x70, 0x71). At the end each gets the
// result (0x6e: the winning team and the reward count it is shown), the pose (0x65), the client constants' team
// reward (pvpBtiRewardItem, pvpBtiRewardWinnerCount or LoserCount; the team result dialog's reward item) and the exp
// of btiTeamWin or btiTeamLose; then the points go back to 0.
public sealed class TeamZoneField : IPvpZoneField {
    private const byte Mode = 3;

    private readonly ILogger logger = Log.Logger.ForContext<TeamZoneField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly TeamZoneState state;
    private readonly int additionalEffectId;
    private readonly Func<int, IEnumerable<FieldPlayer>> playersInBox;
    private readonly Action<IReadOnlyList<long>, int>? onEnd;
    private readonly Random random;

    public int BoxId => state.BoxId;
    public bool Ended {
        get {
            lock (mutex) return state.Ended;
        }
    }

    public TeamZoneField(FieldManager field, int boxId, int prepareSeconds, int matchSeconds, int additionalEffectId, IReadOnlyList<int> boxIds,
                         Func<int, IEnumerable<FieldPlayer>> playersInBox, Action<IReadOnlyList<long>, int>? onEnd, long tick, Random? random = null) {
        this.field = field;
        this.additionalEffectId = additionalEffectId;
        this.playersInBox = playersInBox;
        this.onEnd = onEnd;
        this.random = random ?? Random.Shared;
        state = new TeamZoneState(boxId, tick, prepareSeconds, matchSeconds, boxIds);
    }

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
    }

    public bool IsParticipant(FieldPlayer player) {
        lock (mutex) return state.Active && !state.Ended && state.Entry(player.Value.Character.Id) != null;
    }

    public void Update(long tick) {
        lock (mutex) {
            if (state.DueToActivate(tick)) {
                Activate(tick);
            } else if (state.DueToEnd(tick)) {
                End();
            } else {
                Apply(state.Update(tick, box => playersInBox(box).Where(player => !player.IsDead).Select(player => player.Value.Character.Id).ToList()));
            }
        }
    }

    private void Activate(long tick) {
        List<long> players = playersInBox(state.BoxId).Select(player => player.Value.Character.Id).OrderBy(_ => random.Next()).ToList();
        IReadOnlyList<TeamZoneEntry> entries = state.Activate(players, tick);
        field.Broadcast(PvpPacket.ClearRanking());
        foreach (TeamZoneEntry entry in entries) {
            if (field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                field.Broadcast(PvpPacket.Point(player.ObjectId, entry.Team, 0, Mode));
                if (additionalEffectId > 0) {
                    player.Buffs.AddBuff(field.FieldActor, player, additionalEffectId, 1, tick);
                }
            }
        }
        foreach (TeamZoneEntry entry in entries) {
            field.Broadcast(PvpPacket.TeamRecord(entry.CharacterId, 0, 0));
        }
        field.Broadcast(PvpPacket.TeamScore(0, 0));
        field.Broadcast(PvpPacket.TeamGauge(state.RedGauge()));
        field.Broadcast(PvpPacket.TeamReset());
        logger.Information("Team zone {BoxId} on {MapId} started with {Count} players", state.BoxId, field.MapId, entries.Count);
    }

    public void Damage(FieldPlayer attacker, FieldPlayer victim, long amount) {
        lock (mutex) {
            if (state.Hostile(attacker.Value.Character.Id, victim.Value.Character.Id)) {
                lastHitBy[victim.Value.Character.Id] = (attacker.Value.Character.Id, field.FieldTick);
            }
        }
    }

    // A death counts as a kill for the other team's player that hit the victim within PvpZoneState.KillCreditMs.
    private readonly Dictionary<long, (long Attacker, long Tick)> lastHitBy = [];

    public void Death(FieldPlayer victim) {
        lock (mutex) {
            long killer = 0;
            if (lastHitBy.Remove(victim.Value.Character.Id, out (long Attacker, long Tick) hit) && field.FieldTick - hit.Tick <= PvpZoneState.KillCreditMs) {
                killer = hit.Attacker;
            }
            Apply(state.Death(victim.Value.Character.Id, killer));
            if (killer != 0 && field.TryGetPlayerById(killer, out FieldPlayer? killerPlayer)) {
                field.Broadcast(PlayerKillNoticePacket.Kill(killerPlayer.Value.Character.Name, victim.Value.Character.Name));
            }
        }
    }

    // A participant that comes back is seen by the others with its point again, and sees the teams.
    public void Enter(FieldPlayer player) {
        lock (mutex) {
            if (!state.Active || state.Ended) {
                return;
            }
            state.SetHere(player.Value.Character.Id, true);
            if (state.Entry(player.Value.Character.Id) is { } own) {
                field.Broadcast(PvpPacket.Point(player.ObjectId, own.Team, 0, Mode), player.Session);
            }
            foreach (TeamZoneEntry entry in state.Entries) {
                if (field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? other)) {
                    player.Session.Send(PvpPacket.Point(other.ObjectId, entry.Team, 0, Mode));
                }
                player.Session.Send(PvpPacket.TeamRecord(entry.CharacterId, entry.Kill, entry.Death));
            }
            player.Session.Send(PvpPacket.TeamScore(state.RedScore, state.BlueScore));
            player.Session.Send(PvpPacket.TeamGauge(state.RedGauge()));
            // Last: the team result dialog takes its own team from the point (SetMyTeam).
            if (state.Entry(player.Value.Character.Id) != null) {
                player.Session.Send(PvpPacket.TeamReset());
            }
        }
    }

    public void Leave(FieldPlayer player) {
        lock (mutex) state.SetHere(player.Value.Character.Id, false);
    }

    private void Apply(List<TeamZoneEvent> events) {
        foreach (TeamZoneEvent @event in events) {
            switch (@event.Type) {
                case TeamZoneEventType.Score:
                    field.Broadcast(PvpPacket.TeamScore(state.RedScore, state.BlueScore));
                    field.Broadcast(PvpPacket.TeamGauge(state.RedGauge()));
                    break;
                case TeamZoneEventType.Record:
                    if (state.Entry(@event.CharacterId) is { } entry) {
                        field.Broadcast(PvpPacket.TeamRecord(entry.CharacterId, entry.Kill, entry.Death));
                    }
                    break;
                case TeamZoneEventType.Flag:
                    field.Broadcast(PvpPacket.TeamFlag(@event.Team == TeamZoneState.Red ? 1 : 2, @event.Flag));
                    break;
                case TeamZoneEventType.Plunder:
                    if (field.TryGetPlayerById(@event.CharacterId, out FieldPlayer? plunderer)) {
                        field.Broadcast(PvpPacket.TeamPlunder(plunderer.Value.Character.Name, (byte) (@event.Team == TeamZoneState.Red ? 1 : 2)));
                    }
                    break;
            }
        }
    }

    private void End() {
        int winner = state.End();
        ConstantsTable constants = field.ServerTableMetadata.ConstantsTable;
        var ranking = new List<long>();
        int winners = 0;
        foreach (TeamZoneEntry entry in state.Entries.OrderByDescending(entry => winner != 0 && entry.Team == winner)) {
            bool won = winner != 0 && entry.Team == winner;
            ranking.Add(entry.CharacterId);
            if (won) {
                winners++;
            }
            if (!field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                continue;
            }
            // Rewards only in an arena match's field (the guild and test maps that set the same zone have no match).
            int count = field.Arena == null ? 0 : won ? constants.pvpBtiRewardWinnerCount : constants.pvpBtiRewardLoserCount;
            player.Session.Send(PvpPacket.TeamResult(winner, count));
            player.Session.Send(PvpPacket.WinLose(won));
            try {
                if (count > 0 && constants.pvpBtiRewardItem > 0) {
                    Item? item = field.ItemDrop.CreateItem(constants.pvpBtiRewardItem, amount: count);
                    if (item == null || !player.Session.Item.Inventory.Add(item, notifyNew: true, commit: true)) {
                        logger.Warning("Team zone reward of {CharacterId} not added", entry.CharacterId);
                    }
                }
                if (field.Arena != null) {
                    player.Session.Exp.AddExp(won ? ExpType.btiTeamWin : ExpType.btiTeamLose);
                }
            } catch (Exception ex) {
                logger.Error(ex, "Team zone reward of {CharacterId} not given", entry.CharacterId);
            }
        }
        foreach (TeamZoneEntry entry in state.Entries) {
            if (field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                field.Broadcast(PvpPacket.Point(player.ObjectId, 0, 0, 0));
            }
        }
        logger.Information("Team zone {BoxId} on {MapId} ended: {Red} to {Blue}", state.BoxId, field.MapId, state.RedScore, state.BlueScore);
        onEnd?.Invoke(ranking, winners);
    }
}

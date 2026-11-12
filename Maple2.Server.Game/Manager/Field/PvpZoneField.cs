using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// A trigger's PvP zone in its free-for-all mode (FieldManager.PvpZone, set by set_pvp_zone with type 2): the players
// in the zone's box when the fight starts become participants with their own PvP point (Pvp 0x64), the ranking rows
// (0x67, 0x6a) follow the hits and deaths, the kill log (PlayerKillNotice 0) names each kill, and at the end each
// participant here gets its rank (0x68), the win or loss pose (0x65), its reward and its point back to 0.
// pvp_zone_ended is true from then on. See PvpZoneState for the rules.
public sealed class PvpZoneField : IPvpZoneField {
    private readonly ILogger logger = Log.Logger.ForContext<PvpZoneField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly PvpZoneState state;
    private readonly int additionalEffectId;
    private readonly Func<IEnumerable<FieldPlayer>> playersInBox;
    private readonly Action<IReadOnlyList<long>, int>? onEnd;

    public int BoxId => state.BoxId;
    public bool Ended {
        get {
            lock (mutex) return state.Phase == PvpZonePhase.Ended;
        }
    }

    public PvpZoneField(FieldManager field, int boxId, int prepareSeconds, int matchSeconds, int additionalEffectId,
                        Func<IEnumerable<FieldPlayer>> playersInBox, Action<IReadOnlyList<long>, int>? onEnd, long tick) {
        this.field = field;
        this.additionalEffectId = additionalEffectId;
        this.playersInBox = playersInBox;
        this.onEnd = onEnd;
        ConstantsTable constants = field.ServerTableMetadata.ConstantsTable;
        state = new PvpZoneState(boxId, PvpZoneState.FfaMode, tick, prepareSeconds, matchSeconds, constants.ModePvPScoreKill, constants.ModePvPScoreDead) {
            ComboTickMs = constants.pvpFFALongComboTick,
            SlayerCount = constants.pvpFFASlayerCount,
            RulerCount = constants.pvpFFARulerCount,
        };
    }

    public bool IsParticipant(FieldPlayer player) {
        lock (mutex) {
            return state.Phase == PvpZonePhase.Active && state.Entry(player.Value.Character.Id) != null;
        }
    }

    public bool Hostile(FieldPlayer caster, FieldPlayer target) {
        lock (mutex) {
            return state.Hostile(caster.Value.Character.Id, target.Value.Character.Id);
        }
    }

    public void Update(long tick) {
        lock (mutex) {
            if (state.DueToActivate(tick)) {
                Activate(tick);
            } else if (state.DueToEnd(tick)) {
                End();
            }
        }
    }

    private void Activate(long tick) {
        List<FieldPlayer> players = playersInBox().ToList();
        IReadOnlyList<PvpZoneEntry> entries = state.Activate(players.Select(player => player.Value.Character.Id));
        field.Broadcast(PvpPacket.ClearRanking());
        foreach (PvpZoneEntry entry in entries) {
            if (!field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                continue;
            }
            field.Broadcast(PvpPacket.Point(player.ObjectId, entry.Point, 0, (byte) state.Mode));
            field.Broadcast(PvpPacket.RankRow(entry.CharacterId, 0, 0, 0));
            if (additionalEffectId > 0) {
                player.Buffs.AddBuff(field.FieldActor, player, additionalEffectId, 1, tick);
            }
        }
        logger.Information("PvP zone {BoxId} on {MapId} started with {Count} players", state.BoxId, field.MapId, entries.Count);
    }

    // A player hit another: the attacker's damage row.
    public void Damage(FieldPlayer attacker, FieldPlayer victim, long amount) {
        lock (mutex) {
            PvpZoneEntry? entry = state.Damage(attacker.Value.Character.Id, victim.Value.Character.Id, amount, field.FieldTick);
            if (entry != null) {
                field.Broadcast(PvpPacket.Damage(entry.CharacterId, entry.Damage));
            }
        }
    }

    public void Death(FieldPlayer victim) {
        lock (mutex) {
            List<PvpZoneEntry> changed = state.Death(victim.Value.Character.Id, field.FieldTick, out long killer, out int notice);
            foreach (PvpZoneEntry entry in changed) {
                field.Broadcast(PvpPacket.RankRow(entry.CharacterId, entry.Score, entry.Kill, entry.Death));
            }
            if (killer != 0 && field.TryGetPlayerById(killer, out FieldPlayer? killerPlayer)) {
                field.Broadcast(PlayerKillNoticePacket.Kill(killerPlayer.Value.Character.Name, victim.Value.Character.Name));
                if (notice != 0) {
                    field.Broadcast(PvpPacket.KillNotice(notice, killerPlayer.Value.Character.Name));
                }
            }
        }
    }

    // A newcomer during the fight sees every participant's point and row; a participant that comes back is here again.
    public void Enter(FieldPlayer player) {
        lock (mutex) {
            if (state.Phase != PvpZonePhase.Active) {
                return;
            }
            state.SetHere(player.Value.Character.Id, true);
            // A participant that comes back: the others see its point again.
            if (state.Entry(player.Value.Character.Id) is { } own) {
                field.Broadcast(PvpPacket.Point(player.ObjectId, own.Point, 0, (byte) state.Mode), player.Session);
            }
            foreach (PvpZoneEntry entry in state.Entries) {
                if (field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? other)) {
                    player.Session.Send(PvpPacket.Point(other.ObjectId, entry.Point, 0, (byte) state.Mode));
                }
                player.Session.Send(PvpPacket.RankRow(entry.CharacterId, entry.Score, entry.Kill, entry.Death));
            }
        }
    }

    public void Leave(FieldPlayer player) {
        lock (mutex) {
            state.SetHere(player.Value.Character.Id, false);
        }
    }

    private void End() {
        List<PvpZoneEntry> ranking = state.End();
        ConstantsTable constants = field.ServerTableMetadata.ConstantsTable;
        int[] rewardCounts = [
            constants.PvpFFAReward1Count, constants.PvpFFAReward2Count, constants.PvpFFAReward3Count, constants.PvpFFAReward4Count,
            constants.PvpFFAReward5Count, constants.PvpFFAReward6Count, constants.PvpFFAReward7Count, constants.PvpFFAReward8Count,
            constants.PvpFFAReward9Count, constants.PvpFFAReward10Count,
        ];
        for (int index = 0; index < ranking.Count; index++) {
            PvpZoneEntry entry = ranking[index];
            int rank = index + 1;
            if (!field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                continue;
            }
            player.Session.Send(PvpPacket.LastRank(rank));
            player.Session.Send(PvpPacket.WinLose(rank == 1));
            try {
                Reward(player, rank, PvpZoneState.Reward(rank, entry.Kill, rewardCounts, constants.PvpFFAAdditionRewardRate), constants.PvpFFARewardItem);
            } catch (Exception ex) {
                logger.Error(ex, "PvP zone reward of {CharacterId} not given", entry.CharacterId);
            }
        }
        foreach (PvpZoneEntry entry in ranking) {
            if (field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                field.Broadcast(PvpPacket.Point(player.ObjectId, 0, 0, 0));
            }
        }
        logger.Information("PvP zone {BoxId} on {MapId} ended", state.BoxId, field.MapId);
        onEnd?.Invoke(ranking.Select(entry => entry.CharacterId).ToList(), ranking.Count > 0 ? 1 : 0);
    }

    // The rank's tokens (the result dialog's reward) and the blood mine exp of the rank (the client's commonexptable
    // rows bloodMineRank1 to 3 and bloodMineRankOther).
    private void Reward(FieldPlayer player, int rank, int count, int itemId) {
        if (count > 0 && itemId > 0) {
            Item? item = field.ItemDrop.CreateItem(itemId, amount: count);
            if (item != null && !player.Session.Item.Inventory.Add(item, notifyNew: true, commit: true)) {
                logger.Warning("PvP zone reward {ItemId} x{Count} of {CharacterId} not added", itemId, count, player.Value.Character.Id);
            }
        }
        player.Session.Exp.AddExp(rank switch {
            1 => ExpType.bloodMineRank1,
            2 => ExpType.bloodMineRank2,
            3 => ExpType.bloodMineRank3,
            _ => ExpType.bloodMineRankOther,
        });
    }
}

using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

// A trigger's PvP zone in its free-for-all mode (FieldManager.PvpZone, set by set_pvp_zone with type 2): the players
// in the zone's box when the fight starts become participants with their own PvP point (Pvp 0x64), the ranking rows
// (0x67, 0x6a) follow the hits and deaths, the kill log (PlayerKillNotice 0) names each kill, and at the end each
// participant here gets its rank (0x68), the win or loss pose (0x65), its reward and its point back to 0.
// pvp_zone_ended is true from then on. See PvpZoneState for the rules. The same zone runs 61000035's one punch round
// (type 5, the client's mode 5) with its own reward, and 61000035_me_item's round (type 6) with none.
public sealed class PvpZoneField : IPvpZoneField {
    private readonly ILogger logger = Log.Logger.ForContext<PvpZoneField>();
    private readonly object mutex = new();
    private readonly FieldManager field;
    private readonly PvpZoneState state;
    private readonly int additionalEffectId;
    private readonly Func<IEnumerable<FieldPlayer>> playersInBox;
    // The map's waiting area (the box of its player spawn, apart from the zone's box) and its floor's anchor
    // (FieldManager.WaitingArea); null where the players wait in the zone itself.
    private readonly (TriggerBox Box, Vector3 Floor)? waiting;
    // Chosen here: how far around the floor's anchor a waiting participant is put down.
    private const int FloorRadius = 1500;
    private readonly Action<IReadOnlyList<long>, int>? onEnd;
    // The one punch round's minimum: 61000035_me's script starts the zone only with at least 4 players in box 701
    // (its minimum-players state counts 4 users there, else it times out and moves everyone out); the tokens need as many
    // participants at the start, so a round that lost its players before the fight pays nothing.
    public const int OnePunchMinParticipants = 4;

    public int BoxId => state.BoxId;
    public bool Ended {
        get {
            lock (mutex) return state.Phase == PvpZonePhase.Ended;
        }
    }

    public PvpZoneField(FieldManager field, int boxId, int prepareSeconds, int matchSeconds, int additionalEffectId,
                        Func<IEnumerable<FieldPlayer>> playersInBox, Action<IReadOnlyList<long>, int>? onEnd, long tick,
                        (TriggerBox Box, Vector3 Floor)? waiting = null, int mode = PvpZoneState.FfaMode) {
        this.waiting = waiting;
        this.field = field;
        this.additionalEffectId = additionalEffectId;
        this.playersInBox = playersInBox;
        this.onEnd = onEnd;
        ConstantsTable constants = field.ServerTableMetadata.ConstantsTable;
        state = new PvpZoneState(boxId, mode, tick, prepareSeconds, matchSeconds, constants.ModePvPScoreKill, constants.ModePvPScoreDead) {
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
            // A participant still in the waiting area (65000002's platform above the mine, where box 102's 70000040
            // keeps its skills off) goes down to the zone's floor when the fight starts.
            if (waiting is { } area && area.Box.Contains(player.Position)) {
                player.Session.Send(PortalPacket.MoveByPortal(player, field.FloorSpotAround(area.Floor, FloorRadius), player.Rotation));
            }
            field.Broadcast(PvpPacket.Point(player.ObjectId, entry.Point, 0, (byte) state.Mode));
            field.Broadcast(PvpPacket.RankRow(entry.CharacterId, 0, 0, 0));
            if (additionalEffectId > 0) {
                player.Buffs.AddBuff(field.FieldActor, player, additionalEffectId, 1, tick);
            }
            PvpConditions.Participation(player, field.MapId);
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
            if (changed.Count > 0) {
                PvpConditions.Death(victim, field.MapId);
            }
            if (killer != 0 && field.TryGetPlayerById(killer, out FieldPlayer? killerPlayer)) {
                PvpConditions.Kill(killerPlayer, field.MapId);
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
        (int[] Counts, int AdditionRate, int ItemId)? reward = Rewards(state.Mode, constants);
        for (int index = 0; index < ranking.Count; index++) {
            PvpZoneEntry entry = ranking[index];
            int rank = index + 1;
            if (!field.TryGetPlayerById(entry.CharacterId, out FieldPlayer? player)) {
                continue;
            }
            player.Session.Send(PvpPacket.LastRank(rank));
            player.Session.Send(PvpPacket.WinLose(rank == 1));
            if (rank == 1) {
                PvpConditions.Win(player, field.MapId);
            }
            try {
                if (state.Mode == PvpZoneState.OnePunchMode && reward is { } onePunch && ranking.Count >= OnePunchMinParticipants) {
                    // The one punch round's tokens, to the participants still here: 61000035 is no arena match's field,
                    // and only it sets type 5.
                    Tokens(player, PvpZoneState.Reward(rank, entry.Kill, onePunch.Counts, onePunch.AdditionRate), onePunch.ItemId);
                } else if (field.Arena != null && reward is { } ffa) {
                    // The free-for-all's rewards only in an arena match's field.
                    Tokens(player, PvpZoneState.Reward(rank, entry.Kill, ffa.Counts, ffa.AdditionRate), ffa.ItemId);
                    BloodMineExp(player, rank);
                }
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

    // The result dialog's reward of the zone's mode (UIPvPResultDialog::FUN_141344210, CPvPInfo::GetRewardItemId): the
    // free-for-all's PvpFFAReward<rank>Count plus PvpFFAAdditionRewardRate per kill of PvpFFARewardItem, the one punch
    // round's PvpOnePunchReward<rank>Count with no kill addition of PvpOnePunchRewardItem; none in another mode (the
    // dialog shows 0).
    public static (int[] Counts, int AdditionRate, int ItemId)? Rewards(int mode, ConstantsTable constants) {
        return mode switch {
            PvpZoneState.FfaMode => ([
                constants.PvpFFAReward1Count, constants.PvpFFAReward2Count, constants.PvpFFAReward3Count, constants.PvpFFAReward4Count,
                constants.PvpFFAReward5Count, constants.PvpFFAReward6Count, constants.PvpFFAReward7Count, constants.PvpFFAReward8Count,
                constants.PvpFFAReward9Count, constants.PvpFFAReward10Count,
            ], constants.PvpFFAAdditionRewardRate, constants.PvpFFARewardItem),
            PvpZoneState.OnePunchMode => ([
                constants.PvpOnePunchReward1Count, constants.PvpOnePunchReward2Count, constants.PvpOnePunchReward3Count,
                constants.PvpOnePunchReward4Count, constants.PvpOnePunchReward5Count, constants.PvpOnePunchReward6Count,
                constants.PvpOnePunchReward7Count, constants.PvpOnePunchReward8Count, constants.PvpOnePunchReward9Count,
                constants.PvpOnePunchReward10Count,
            ], 0, constants.PvpOnePunchRewardItem),
            _ => null,
        };
    }

    // The rank's tokens (the result dialog's reward).
    private void Tokens(FieldPlayer player, int count, int itemId) {
        if (count > 0 && itemId > 0) {
            Item? item = field.ItemDrop.CreateItem(itemId, amount: count);
            if (item != null && !player.Session.Item.Inventory.Add(item, notifyNew: true, commit: true)) {
                logger.Warning("PvP zone reward {ItemId} x{Count} of {CharacterId} not added", itemId, count, player.Value.Character.Id);
            }
        }
    }

    // The blood mine exp of the rank (the client's commonexptable rows bloodMineRank1 to 3 and bloodMineRankOther).
    private static void BloodMineExp(FieldPlayer player, int rank) {
        player.Session.Exp.AddExp(rank switch {
            1 => ExpType.bloodMineRank1,
            2 => ExpType.bloodMineRank2,
            3 => ExpType.bloodMineRank3,
            _ => ExpType.bloodMineRankOther,
        });
    }
}

using System.Collections.Concurrent;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

public class DungeonFieldManager : FieldManager {
    private readonly ILogger resultLogger = Log.Logger.ForContext<DungeonFieldManager>();
    public readonly DungeonRoomMetadata DungeonMetadata;
    public DungeonFieldManager? Lobby { get; init; }
    public readonly ConcurrentDictionary<int, DungeonFieldManager> RoomFields = [];
    public required DungeonRoomRecord DungeonRoomRecord { get; init; }
    public int PartyId { get; init; }

    public DungeonFieldManager(DungeonRoomMetadata dungeonMetadata, MapMetadata mapMetadata, UgcMapMetadata ugcMetadata, MapEntityMetadata entities, NpcMetadataStorage npcMetadata, long ownerId = 0, int size = 1, int partyId = 0)
        : base(mapMetadata, ugcMetadata, entities, npcMetadata, ownerId) {
        DungeonMetadata = dungeonMetadata;
        if (dungeonMetadata.LobbyFieldId == mapMetadata.Id) {
            Lobby = this;
        }
        DungeonId = dungeonMetadata.Id;
        Size = size;
        PartyId = partyId;
    }

    /// <summary>
    /// The room timer of a dungeon room (DungeonRoom timerType, durationTick): none for timerType none or no duration.
    /// </summary>
    public static RoomTimerType? RoomTimerTypeOf(DungeonTimerType timerType, int durationTick) {
        if (durationTick <= 0) {
            return null;
        }
        return timerType switch {
            DungeonTimerType.clock => RoomTimerType.Clock,
            DungeonTimerType.gauge => RoomTimerType.Gauge,
            _ => null,
        };
    }

    // isExpireTimeOut: the room is closed when its time runs out. Without it the time running out
    // is only the dungeon_timeout condition, and the room's script decides what follows.
    public void StartRoomTimer() {
        if (RoomTimerTypeOf(DungeonMetadata.TimerType, DungeonMetadata.DurationTick) is not RoomTimerType type) {
            return;
        }
        SetRoomTimer(type, DungeonMetadata.DurationTick, DungeonMetadata.IsExpireTimeOut);
    }

    /// <summary>
    /// The room gets its result. showResult false (dungeon_clear uiType None) leaves out the result banner; the
    /// clear still counts, so a Colosseum run that ends after some rounds keeps them.
    /// </summary>
    public void ChangeState(DungeonState state, bool showResult = true) {
        if (!TakesResult(DungeonRoomRecord.State, DungeonMetadata.GroupType)) {
            resultLogger.Debug("Dungeon {DungeonId} already has its result {State}; {NewState} ignored", DungeonId, DungeonRoomRecord.State, state);
            return;
        }
        if (state == DungeonState.Clear && DungeonRoomRecord.State == DungeonState.None) {
            long seconds = (FieldTick - DungeonRoomRecord.StartTick) / 1000;
            foreach ((long characterId, List<DungeonMission> missions) in ScorePlayTime(DungeonRoomRecord, seconds)) {
                if (TryGetPlayerById(characterId, out FieldPlayer? player)) {
                    player.Session.Send(DungeonMissionPacket.Update(missions.ToArray()));
                }
            }
            KeepBossClear(seconds);
            KeepColosseumRun(seconds);
        }
        DungeonRoomRecord.State = state;
        DungeonRoomRecord.EndTick = FieldTick;

        var compiledResults = new Dictionary<long, DungeonUserResult>();

        // Get player's best record
        foreach ((long characterId, DungeonUserRecord userRecord) in DungeonRoomRecord.UserResults) {
            if (compiledResults.ContainsKey(characterId)) {
                continue;
            }

            if (!TryGetPlayerById(characterId, out FieldPlayer? _)) {
                continue;
            }

            List<KeyValuePair<DungeonAccumulationRecordType, int>> recordkvp = userRecord.AccumulationRecords.OrderByDescending(record => record.Value).ToList();
            foreach (KeyValuePair<DungeonAccumulationRecordType, int> kvp in recordkvp) {
                if (compiledResults.ContainsKey(characterId) || compiledResults.Values.Any(x => x.RecordType == kvp.Key)) {
                    continue;
                }

                compiledResults.Add(characterId, new DungeonUserResult(characterId, kvp.Key, kvp.Value + 1));
                break;
            }
        }

        if (showResult) {
            Broadcast(DungeonRoomPacket.DungeonResult(DungeonRoomRecord.State, compiledResults));
        }

        if (DungeonRoomRecord.State == DungeonState.Clear) {
            long clearTimestamp = DateTime.Now.ToEpochSeconds();
            foreach (long characterId in compiledResults.Keys) {
                if (!TryGetPlayerById(characterId, out FieldPlayer? player)) {
                    continue;
                }

                player.Session.Dungeon.CompleteDungeon(clearTimestamp);
                player.Session.ConditionUpdate(ConditionType.dungeon_clear,
                    codeLong: DungeonId);

            }
        }
    }
    /// <summary>
    /// Counts a mission event of one player: in that player's user missions, and in the party missions of
    /// every player of the room. Returns the changed missions per character. Nothing counts once the room
    /// has a result.
    /// </summary>
    public static Dictionary<long, List<DungeonMission>> CountMissions(DungeonRoomRecord room, long characterId, Func<DungeonMission, bool> step) {
        var changed = new Dictionary<long, List<DungeonMission>>();
        if (room.State != DungeonState.None) {
            return changed;
        }
        foreach ((long recordId, DungeonUserRecord record) in room.UserResults) {
            foreach (DungeonMission mission in record.Missions.Values) {
                if (mission.IsUserMission && recordId != characterId) {
                    continue;
                }
                if (step(mission)) {
                    if (!changed.TryGetValue(recordId, out List<DungeonMission>? list)) {
                        changed[recordId] = list = [];
                    }
                    list.Add(mission);
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// A room takes one result: a later dungeon_clear or dungeon_fail changes nothing (no second result packet,
    /// clear count or condition). The Colosseum is left out: its script goes back to its start after each end,
    /// and a new run in the same room ends with its own clear or fail.
    /// </summary>
    public static bool TakesResult(DungeonState current, DungeonGroupType groupType) =>
        current == DungeonState.None || groupType == DungeonGroupType.colosseum;

    /// <summary>
    /// Whether a clear of the room goes on a boss board: the room has a BossRanking and its group is one whose
    /// board asks for the clears (the ranking dialog's boss boards 7, 8, 9, 0xf and 0x11 list the rooms of
    /// these groups that have a BossRanking, and ask for 0x47/0x48/0x51/0x5b with it; its fame challenge board
    /// 0xc lists the fameChallenge rooms that have one, and asks for 0x8d/0x8e/0x97/0xa1/0xa2 with it).
    /// </summary>
    public static bool KeepsBossClear(DungeonRoomMetadata metadata) =>
        metadata.BossRanking != 0 && metadata.GroupType is DungeonGroupType.raid or DungeonGroupType.chaosRaid
            or DungeonGroupType.reverseRaid or DungeonGroupType.lapenta or DungeonGroupType.turka
            or DungeonGroupType.fameChallenge;

    /// <summary>A player of the room at its clear: the character, its party id (0 without one) and that party's leader.</summary>
    public readonly record struct BossClearPlayer(long CharacterId, int PartyId, long PartyLeaderId);

    /// <summary>
    /// The party of a boss clear: with a room entered by a party, the players of that party in the field, led by
    /// its leader when the leader is one of them, else by the first of them. When none of them is in the field (the
    /// party broke up or was left during the run), or the room was entered without a party, the room's first user
    /// alone, when in the field. Null when nobody is left to keep.
    /// </summary>
    public static (long LeaderId, long[] Members)? BossClearParty(int roomPartyId, long firstCharacterId, IEnumerable<BossClearPlayer> players) {
        BossClearPlayer[] present = players.ToArray();
        if (roomPartyId != 0) {
            BossClearPlayer[] party = present.Where(player => player.PartyId == roomPartyId).ToArray();
            if (party.Length > 0) {
                long leaderId = party.Any(player => player.CharacterId == party[0].PartyLeaderId)
                    ? party[0].PartyLeaderId
                    : party[0].CharacterId;
                return (leaderId, party.Select(player => player.CharacterId).ToArray());
            }
        }
        foreach (BossClearPlayer player in present) {
            if (player.CharacterId == firstCharacterId) {
                return (player.CharacterId, [player.CharacterId]);
            }
        }
        return null;
    }

    // The first clear of a room with a boss board keeps one row of the party in the field.
    private void KeepBossClear(long seconds) {
        if (!KeepsBossClear(DungeonMetadata)) {
            return;
        }
        var players = new List<BossClearPlayer>();
        foreach (long characterId in DungeonRoomRecord.UserResults.Keys) {
            if (TryGetPlayerById(characterId, out FieldPlayer? player)) {
                Party? party = player.Session.Party.Party;
                players.Add(new BossClearPlayer(characterId, party?.Id ?? 0, party?.LeaderCharacterId ?? 0));
            }
        }
        if (BossClearParty(PartyId, DungeonRoomRecord.FirstCharacterId, players) is not { } clear) {
            resultLogger.Debug("Dungeon {DungeonId} boss clear not kept: no player of the room is in the field", DungeonId);
            return;
        }
        if (!DungeonRoomRecord.TryKeepBossClear()) {
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        long clearId = db.AddBossClear(DungeonMetadata.BossRanking, DungeonId, clear.LeaderId, (int) Math.Max(0, seconds),
            DateTime.Now.ToEpochSeconds(), clear.Members);
        resultLogger.Debug("Dungeon {DungeonId} boss clear {ClearId} kept for {Count} members", DungeonId, clearId, clear.Members.Length);
    }

    /// <summary>
    /// The run of a Colosseum room that goes on the Colosseum board (the ranking dialog's board 0x10 lists the
    /// colosseum rooms that have a BossRanking): the room's first user and the rounds it cleared (dungeon_clear_round),
    /// when it cleared any and the script did not disable the ranking (dungeon_disable_ranking, a continued game).
    /// </summary>
    public static (long CharacterId, int Rounds)? ColosseumRun(DungeonRoomMetadata metadata, DungeonRoomRecord room) {
        if (metadata.GroupType != DungeonGroupType.colosseum || metadata.BossRanking == 0 || room.RankingDisabled) {
            return null;
        }
        if (!room.UserResults.TryGetValue(room.FirstCharacterId, out DungeonUserRecord? record) || record.Round <= 0) {
            return null;
        }
        return (room.FirstCharacterId, record.Round);
    }

    // The first end of a Colosseum room keeps the run as the character's best of the season, when it is in the
    // field. A run outside every Colosseum season keeps nothing.
    private void KeepColosseumRun(long seconds) {
        if (ColosseumRun(DungeonMetadata, DungeonRoomRecord) is not { } run || !TryGetPlayerById(run.CharacterId, out FieldPlayer? _)) {
            return;
        }
        DateTime now = DateTime.Now;
        int seasonId = ColosseumBest.SeasonAt(TableMetadata.SeasonDataTable.Boss, now);
        if (seasonId == 0) {
            resultLogger.Debug("Dungeon {DungeonId} colosseum run not kept: no season at {Time}", DungeonId, now);
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        bool saved = db.SaveColosseumRun(run.CharacterId, seasonId, run.Rounds, (int) Math.Max(0, seconds), now.ToEpochSeconds());
        resultLogger.Debug("Dungeon {DungeonId} colosseum run {Rounds} rounds in {Seconds}s, season {SeasonId}: best {Saved}",
            DungeonId, run.Rounds, seconds, seasonId, saved);
    }

    /// <summary>PlayTime missions of every player of the room, scored at the clear. Returns the changed missions.</summary>
    public static Dictionary<long, List<DungeonMission>> ScorePlayTime(DungeonRoomRecord room, long seconds) {
        var changed = new Dictionary<long, List<DungeonMission>>();
        foreach ((long characterId, DungeonUserRecord record) in room.UserResults) {
            foreach (DungeonMission mission in record.Missions.Values) {
                if (mission.ScorePlayTime(seconds)) {
                    if (!changed.TryGetValue(characterId, out List<DungeonMission>? list)) {
                        changed[characterId] = list = [];
                    }
                    list.Add(mission);
                }
            }
        }
        return changed;
    }

    public void CountMissions(FieldPlayer player, Func<DungeonMission, bool> step) {
        foreach ((long characterId, List<DungeonMission> missions) in CountMissions(DungeonRoomRecord, player.Value.Character.Id, step)) {
            foreach (FieldPlayer roomPlayer in Players.Values) {
                if (roomPlayer.Value.Character.Id == characterId) {
                    roomPlayer.Session.Send(DungeonMissionPacket.Update(missions.ToArray()));
                }
            }
        }
    }
}

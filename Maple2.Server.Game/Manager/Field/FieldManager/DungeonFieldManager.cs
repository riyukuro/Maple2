using System.Collections.Concurrent;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
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

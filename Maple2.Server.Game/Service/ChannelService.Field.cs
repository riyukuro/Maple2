using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<FieldResponse> Field(FieldRequest request, ServerCallContext context) {
        switch (request.FieldCase) {
            case FieldRequest.FieldOneofCase.CreateDungeon:
                return Task.FromResult(CreateDungeon(request.CreateDungeon, request.RequesterId));
            case FieldRequest.FieldOneofCase.DestroyDungeon:
                return Task.FromResult(DestroyDungeon(request.DestroyDungeon));
            case FieldRequest.FieldOneofCase.CreatePvpMatch:
                return Task.FromResult(CreatePvpMatch(request.CreatePvpMatch));
            case FieldRequest.FieldOneofCase.CheckDungeon:
                return Task.FromResult(new FieldResponse {
                    Error = (int) FieldEntryRules.DungeonRoomError(server.HasDungeon(request.CheckDungeon.RoomId)),
                    RoomId = request.CheckDungeon.RoomId,
                });
            default:
                return Task.FromResult(new FieldResponse());
        }
    }

    private FieldResponse CreateDungeon(FieldRequest.Types.CreateDungeon create, long requestorId) {
        if (!tableMetadata.DungeonRoomTable.Entries.TryGetValue(create.DungeonId, out DungeonRoomMetadata? dungeonRoom)) {
            return new FieldResponse {
                Error = (int) MigrationError.s_move_err_dungeon_not_exist,
            };
        }

        // A room that has closed since it was asked for (a match found later) is not made; the requester's own
        // session tells, when it is on this channel.
        if (server.GetSession(requestorId, out GameSession? requester) && requester.Dungeon.ClosedError(dungeonRoom) != null) {
            return new FieldResponse {
                Error = (int) MigrationError.s_move_err_FailCreateDungeon,
            };
        }

        DungeonFieldManager? dungeonField = server.CreateDungeon(dungeonRoom, requestorId, create.Size, create.PartyId);
        if (dungeonField == null) {
            return new FieldResponse {
                Error = (int) MigrationError.s_move_err_FailCreateDungeon,
            };
        }

        return new FieldResponse {
            Error = (int) MigrationError.ok,
            RoomId = dungeonField.RoomId,
        };
    }

    // The rank duel match field (World PvpLookup): one field of a rank duel map for the two characters.
    private FieldResponse CreatePvpMatch(FieldRequest.Types.CreatePvpMatch create) {
        if (create.CharacterIds.Count != 2) {
            return new FieldResponse { Error = (int) MigrationError.s_move_err_FailCreateDungeon };
        }
        FieldManager? field = server.CreateRankDuel(create.MatchId, create.CharacterIds[0], create.CharacterIds[1]);
        if (field == null) {
            return new FieldResponse { Error = (int) MigrationError.s_move_err_FailCreateDungeon };
        }
        return new FieldResponse {
            Error = (int) MigrationError.ok,
            MapId = field.MapId,
            RoomId = field.RoomId,
        };
    }

    private FieldResponse DestroyDungeon(FieldRequest.Types.DestroyDungeon destroy) {
        MigrationError error = server.DestroyDungeon(destroy.RoomId);

        return new FieldResponse {
            Error = (int) error,
        };
    }
}

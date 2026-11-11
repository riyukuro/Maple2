using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Numerics;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;
using MigrationType = Maple2.Server.World.Service.MigrationType;

namespace Maple2.Server.Game.Util;

public static class PlayerWarpUtil {
    // Vertical offset to prevent players from spawning inside the ground or other players
    private const float VERTICAL_SPAWN_OFFSET = 75f;

    // The address a channel change goes to; false for a text that is no IP address or a port outside 1-65535.
    public static bool TryEndpoint(string? address, int port, [NotNullWhen(true)] out IPEndPoint? endpoint) {
        return EndpointHelper.TryEndpoint(address, port, out endpoint);
    }

    public enum GoToKind { Portal, Field, Channel }

    // How the player gets to the target. The channel is tested first: room ids and map ids repeat on every
    // channel, so the same room id and map id on another channel is still another place.
    // Every home has the same map id, so two homes are told apart by their owners (0: not a home).
    public static GoToKind Decide(int targetChannel, int ownChannel, int targetRoomId, int ownRoomId, int targetMapId, int ownMapId, long targetOwnerId = 0, long ownOwnerId = 0) {
        if (targetChannel != ownChannel) {
            return GoToKind.Channel;
        }
        return targetRoomId == ownRoomId && targetMapId == ownMapId && targetOwnerId == ownOwnerId ? GoToKind.Portal : GoToKind.Field;
    }

    // The home a move to a player goes into: the home the target is in when the move may enter homes, else
    // none (0), which for the home map means the mover's own home (GameSession.PrepareField).
    public static long TargetHome(bool intoHome, long targetOwnerId) {
        return intoHome ? targetOwnerId : 0;
    }

    /// <summary>
    /// Moves the player to where the target player stands: same room, another room on this channel, or another channel.
    /// Returns false when the target's position could not be found or the field could not be prepared.
    /// intoHome: the target's home field is entered when the target is in one, whatever its entry rules. Only
    /// the recall invite of a GM sets it.
    /// </summary>
    public static bool GoToPlayer(GameSession session, PlayerInfo playerInfo, bool intoHome = false) {
        if (session.Field is null) return false;

        PlayerWarpResponse? warpResponse = session.World.PlayerWarp(new PlayerWarpRequest {
            RequesterId = session.CharacterId,
            GoToPlayer = new PlayerWarpRequest.Types.GoToPlayer {
                Channel = playerInfo.Channel,
                CharacterId = playerInfo.CharacterId,
            },
        });

        if (warpResponse is not { Error: 0 }) {
            return false;
        }

        var position = new Vector3(warpResponse.X, warpResponse.Y, warpResponse.Z + VERTICAL_SPAWN_OFFSET);
        long ownerId = TargetHome(intoHome, warpResponse.OwnerId);
        long ownOwnerId = session.Field is Manager.Field.HomeFieldManager ownHome ? ownHome.OwnerId : 0;
        GoToKind kind = intoHome
            ? Decide(playerInfo.Channel, session.Player.Value.Character.Channel, warpResponse.RoomId, session.Field.RoomId,
                playerInfo.MapId, session.Player.Value.Character.MapId, ownerId, ownOwnerId)
            : Decide(playerInfo.Channel, session.Player.Value.Character.Channel, warpResponse.RoomId, session.Field.RoomId,
                playerInfo.MapId, session.Player.Value.Character.MapId);
        if (kind == GoToKind.Portal) {
            session.Send(PortalPacket.MoveByPortal(session.Player, position, session.Player.Rotation));
            return true;
        }

        if (kind == GoToKind.Field) {
            if (!session.PrepareField(playerInfo.MapId, ownerId: ownerId, roomId: warpResponse.RoomId, position: position)) {
                session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_default));
                return false;
            }
            session.Send(FieldEnterPacket.Request(session.Player));
            return true;
        }

        session.MigrationSave();
        bool moved = false;
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                Channel = playerInfo.Channel,
                MapId = playerInfo.MapId,
                RoomId = warpResponse.RoomId,
                PositionX = position.X,
                PositionY = position.Y,
                PositionZ = position.Z,
                Type = MigrationType.Normal,
            };

            if (ownerId != 0) {
                request.OwnerId = ownerId;
            }

            MigrateOutResponse response = session.World.MigrateOut(request);
            if (TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                session.Send(MigrationPacket.GameToGame(endpoint, response.Token, playerInfo.MapId));
                session.State = SessionState.ChangeMap;
                moved = true;
            } else {
                // Refused like a failed call: the player is told and nothing is thrown at the caller.
                Log.Error("GoToPlayer: channel {Channel} answered with the address '{Address}' port {Port}", playerInfo.Channel, response.IpAddress, response.Port);
                session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            }
        } catch (RpcException ex) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            session.Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            session.Disconnect();
        }
        return moved; // false: the World server refused the channel change
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Numerics;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
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
        endpoint = null;
        if (port is < 1 or > IPEndPoint.MaxPort || !IPAddress.TryParse(address, out IPAddress? ip)) {
            return false;
        }
        endpoint = new IPEndPoint(ip, port);
        return true;
    }

    /// <summary>
    /// Moves the player to where the target player stands: same room, another room on this channel, or another channel.
    /// Returns false when the target's position could not be found or the field could not be prepared.
    /// </summary>
    public static bool GoToPlayer(GameSession session, PlayerInfo playerInfo) {
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
        if (warpResponse.RoomId == session.Field.RoomId && playerInfo.MapId == session.Player.Value.Character.MapId) {
            session.Send(PortalPacket.MoveByPortal(session.Player, position, session.Player.Rotation));
            return true;
        }

        if (playerInfo.Channel == session.Player.Value.Character.Channel) {
            if (!session.PrepareField(playerInfo.MapId, roomId: warpResponse.RoomId, position: position)) {
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

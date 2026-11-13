using System.Net;
using Maple2.Server.Game.Util;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class ChannelHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.Channel;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        short channel = packet.ReadShort();
        // The client sends no request for its own channel, from a dungeon or random room, in combat or while dead.
        if (session.Field == null || !ChannelRules.CanChange(GameServer.GetChannel(), channel, session.Player.IsDead,
                session.Player.InBattle, session.Field.FieldType == FieldType.Default)) {
            return;
        }
        session.MigrationSave();
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Game,
                Channel = channel,
                ExactChannel = true,
                PetUid = session.SummonedPetUid,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Channel change", response.IpAddress, response.Port);
                Refuse(session, MigrationError.s_move_err_default);
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, session.Field?.MapId ?? 0));
            session.State = SessionState.ChangeChannel;
            session.Disconnect();
        } catch (RpcException ex) {
            Logger.Warning("Channel change to {Channel} refused by the World server: {Status}", channel, ex.StatusCode);
            Refuse(session, ex.StatusCode switch {
                StatusCode.Unavailable => MigrationError.s_move_err_no_server,
                StatusCode.ResourceExhausted => MigrationError.s_move_err_member_limit,
                _ => MigrationError.s_move_err_default,
            });
        }
    }

    // A refused change: the client shows its move error box (CNetworkService::OnGameToGame) and the player plays on
    // in this channel, with the channel list as it is now.
    private void Refuse(GameSession session, MigrationError error) {
        session.MigrationRefused();
        session.Send(MigrationPacket.GameToGameError(error));
        try {
            ChannelsResponse channels = World.Channels(new ChannelsRequest());
            session.Send(ChannelPacket.Load(channels.Channels));
        } catch (RpcException ex) {
            Logger.Warning("Channel list after a refused change not sent: {Status}", ex.StatusCode);
        }
    }
}

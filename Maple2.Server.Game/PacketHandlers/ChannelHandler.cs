using System.Net;
using Maple2.Server.Game.Util;
using Grpc.Core;
using Maple2.Model.Enum;
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
        // The client sends no request for its own channel and none while its player is dead.
        if (session.Field == null || !ChannelRules.CanChange(GameServer.GetChannel(), channel, session.Player.IsDead)) {
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
                PetUid = session.SummonedPetUid,
            };

            MigrateOutResponse response = World.MigrateOut(request);
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Channel change", response.IpAddress, response.Port);
                session.Send(NoticePacket.MessageBox(new InterfaceText(StringCode.s_msg_cant_movechannel)));
                session.CutAfterSend(); // the box does not make the client leave
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, session.Field?.MapId ?? 0));
            session.State = SessionState.ChangeChannel;
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to migrate to channel {Channel}", channel);

            session.Send(NoticePacket.MessageBox(new InterfaceText(StringCode.s_msg_cant_movechannel)));

            // Update the client with the latest channel list.
            ChannelsResponse response = World.Channels(new ChannelsRequest());
            session.Send(ChannelPacket.Load(response.Channels));
            session.CutAfterSend();
        } finally {
            session.Disconnect();
        }
    }
}

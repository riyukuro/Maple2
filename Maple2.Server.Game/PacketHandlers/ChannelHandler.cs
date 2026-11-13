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

    // Milliseconds after a refused change during which another one is refused with no save.
    internal const long RefusalWait = 2000;

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
        // The client asks again as often as the player clicks (WorldService::FUN_141bc80c0 has no wait), and each
        // ask that reaches the World saves the whole session first: a change asked within RefusalWait of a refused
        // one gets that refusal again at once, with no save and no World call.
        long now = Environment.TickCount64;
        if (session.ChannelChangeRefusedAt != 0 && now - session.ChannelChangeRefusedAt < RefusalWait) {
            session.Send(MigrationPacket.GameToGameError(session.ChannelChangeRefusedError));
            return;
        }
        // A channel the World does not list is refused before the save: the World sends a channel it lacks to the
        // first one (a login pick may be a gap), while the client asks only for a channel of its list.
        ChannelsResponse listed;
        try {
            listed = World.Channels(new ChannelsRequest());
        } catch (RpcException ex) {
            Logger.Warning("Channel change to {Channel}: no channel list from the World server: {Status}", channel, ex.StatusCode);
            Refuse(session, MigrationError.s_move_err_no_server, null);
            return;
        }
        if (!listed.Channels.Contains(channel)) {
            Refuse(session, MigrationError.s_move_err_no_server, listed);
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
                session.MigrationRefused();
                Refuse(session, MigrationError.s_move_err_default, Channels());
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, session.Field?.MapId ?? 0));
            session.State = SessionState.ChangeChannel;
            session.Disconnect();
        } catch (RpcException ex) {
            Logger.Warning("Channel change to {Channel} refused by the World server: {Status}", channel, ex.StatusCode);
            session.MigrationRefused();
            Refuse(session, ex.StatusCode switch {
                StatusCode.Unavailable => MigrationError.s_move_err_no_server,
                StatusCode.ResourceExhausted => MigrationError.s_move_err_member_limit,
                _ => MigrationError.s_move_err_default,
            }, Channels());
        }
    }

    // A refused change: the client shows its move error box (CNetworkService::OnGameToGame) and the player plays on
    // in this channel, with the channel list as it is now (none when the World gave none).
    private static void Refuse(GameSession session, MigrationError error, ChannelsResponse? channels) {
        session.ChannelChangeRefusedAt = Environment.TickCount64;
        session.ChannelChangeRefusedError = error;
        session.Send(MigrationPacket.GameToGameError(error));
        if (channels != null) {
            session.Send(ChannelPacket.Load(channels.Channels));
        }
    }

    private ChannelsResponse? Channels() {
        try {
            return World.Channels(new ChannelsRequest());
        } catch (RpcException ex) {
            Logger.Warning("Channel list after a refused change not sent: {Status}", ex.StatusCode);
            return null;
        }
    }
}

using System.Net;
using Maple2.Server.Game.Util;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class GlobalPortal : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.GlobalPortal;

    private enum Command : byte {
        Enter = 2,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }

    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Enter:
                HandleEnter(session, packet);
                return;
        }
    }

    private void HandleEnter(GameSession session, IByteReader packet) {
        int eventId = packet.ReadInt();
        int index = packet.ReadInt();

        // The client's own notice for a dead player (its dialog mode 3).
        if (session.Player.IsDead) {
            session.Send(GlobalPortalPacket.CannotEnterDead());
            return;
        }
        // Below GlobalPortalMinLevel the dialog was not shown; an enter that comes anyway gets the dialog's level notice.
        if (!session.GlobalPortalLevelAllowed()) {
            session.Send(GlobalPortalPacket.CannotEnterLevel());
            return;
        }
        if (!GlobalPortalRules.EntryValid(index, GlobalPortalRules.CaptionCount)) {
            return;
        }

        // A World server that does not answer is a portal that cannot be joined now, as one it refuses (no answer
        // sent, the player stays); the exception used to end the connection.
        TimeEventResponse eventResponse;
        try {
            eventResponse = World.TimeEvent(new TimeEventRequest {
                JoinGlobalPortal = new TimeEventRequest.Types.JoinGlobalPortal {
                    EventId = eventId,
                    Index = index,
                },
            }, deadline: DateTime.UtcNow.Add(ChannelHandler.MoveCallDeadline));
        } catch (RpcException ex) {
            Logger.Warning("Global portal {EventId} enter: the World server did not answer: {Status}", eventId, ex.StatusCode);
            return;
        }

        if (eventResponse.Error != 0 || eventResponse.GlobalPortalInfo == null) {
            return;
        }

        GlobalPortalInfo portal = eventResponse.GlobalPortalInfo;

        // A save that did not store refuses the move (GameSession.MigrationSave).
        if (!session.MigrationSave()) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            return;
        }
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Channel = portal.Channel,
                Server = Server.World.Service.Server.Game,
                RoomId = portal.RoomId,
                MapId = portal.MapId,
                PortalId = portal.PortalId,
                PetUid = session.SummonedPetUid,
                PetAttackOff = session.PetAttackOff,
            };

            MigrateOutResponse response = World.MigrateOut(request, deadline: DateTime.UtcNow.Add(ChannelHandler.MoveCallDeadline));
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Global portal", response.IpAddress, response.Port);
                session.MigrationRefused();
                session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, eventResponse.GlobalPortalInfo.MapId));
            session.State = SessionState.ChangeMap;
            session.Disconnect();
        } catch (RpcException ex) {
            // A refused move (the client shows its move error box) leaves the player where they stand, as
            // GameSession.Migrate does; it used to end the connection.
            Logger.Warning("Global portal move to {MapId} refused by the World server: {Status}", portal.MapId, ex.StatusCode);
            session.MigrationRefused();
            session.Send(MigrationPacket.GameToGameError(GameSession.MoveRefusedError(ex.StatusCode)));
        }
    }
}

using System.Net;
using Maple2.Server.Game.Util;
using Grpc.Core;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using static Maple2.Model.Error.MigrationError;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

// On the field thread, as the channel change (ChannelHandler): the save holds what the packets before it gave.
public class QuitHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestQuit;

    // Queued on the field thread, and the receive thread waits for it (FieldPacketHandler.QueueAndWait).
    public override bool TryHandleDeferred(GameSession session, IByteReader reader) => QueueAndWait(session, reader);

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        bool quitGame = packet.ReadBool();

        // Reset map to return map
        int mapBefore = session.Player.Value.Character.MapId;
        if (session.Player.Value.Character.ReturnMaps.Peek() is not 0) {
            session.Player.Value.Character.MapId = session.Player.Value.Character.ReturnMaps.Peek();
        }

        // Fully close client
        if (quitGame) {
            session.Disconnect();
            return;
        }

        // A save that did not store sends no client to the character list, which would load the state from before it
        // (GameSession.MigrationSave): the client is told as for a refused quit and the session is ended, its Dispose
        // trying one last save, which stores the map the character is on (the return map set above is put back). A quit
        // the World refuses after the save stored puts it back as well before the session ends.
        void Undo() => session.Player.Value.Character.MapId = mapBefore;
        if (!session.MigrationSave(Undo)) {
            session.Send(MigrationPacket.GameToLoginError(s_move_err_default));
            session.CutAfterSend();
            return;
        }
        try {
            var request = new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Server = Server.World.Service.Server.Login,
            };

            MigrateOutResponse response = World.MigrateOut(request, deadline: DateTime.UtcNow.Add(ChannelHandler.MoveCallDeadline));
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Quit to login", response.IpAddress, response.Port);
                Undo();
                session.Send(MigrationPacket.GameToLoginError(s_move_err_default));
                session.CutAfterSend();
                return;
            }
            session.Send(MigrationPacket.GameToLogin(endpoint, response.Token));
        } catch (RpcException ex) {
            Logger.Error(ex, "MigrateOut failed for account={AccountId} char={CharacterId}",
                session.AccountId, session.CharacterId);
            Undo();
            session.Send(MigrationPacket.GameToLoginError(s_move_err_default));
            session.CutAfterSend();
        } finally {
            session.Disconnect();
        }
    }
}

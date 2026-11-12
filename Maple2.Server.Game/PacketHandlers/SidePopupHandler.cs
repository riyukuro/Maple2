using System.Net;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

// UISidePopupTalk "onEnterFieldWar" (uisidepopuptalk_1.cpp): the field war announce's button. The packet has no war
// id, so the war is the one this player was announced (GameSession.FieldWarUid).
public class SidePopupHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SidePopup;

    private enum Command : byte {
        EnterFieldWar = 1,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.EnterFieldWar:
                HandleEnterFieldWar(session);
                return;
        }
    }

    // Chosen here: the enter checks again what the announce did (the player may have changed field or level since),
    // and a dead player stays.
    private void HandleEnterFieldWar(GameSession session) {
        if (session.FieldWarUid == 0 || session.Player.IsDead || session.Field == null
            || !TimeEventRules.PortalAnnouncedIn(session.Field.Metadata.Property.Type) || !session.FieldWarEligible()) {
            return;
        }

        TimeEventResponse eventResponse;
        try {
            eventResponse = World.TimeEvent(new TimeEventRequest {
                JoinFieldWar = new TimeEventRequest.Types.JoinFieldWar {
                    EventId = session.FieldWarUid,
                },
            });
        } catch (RpcException ex) {
            Logger.Error("Field war join: {Message}", ex.Message);
            return;
        }
        if (eventResponse.Error != 0 || eventResponse.FieldWarInfo is not { } war) {
            return;
        }

        session.MigrationSave();
        try {
            MigrateOutResponse response = World.MigrateOut(new MigrateOutRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                MachineId = session.MachineId.ToString(),
                Channel = war.Channel,
                Server = Server.World.Service.Server.Game,
                RoomId = war.RoomId,
                MapId = war.MapId,
                PetUid = session.SummonedPetUid,
            });
            if (!PlayerWarpUtil.TryEndpoint(response.IpAddress, response.Port, out IPEndPoint? endpoint)) {
                Logger.Error("{Where}: the World server answered with the address '{Address}' port {Port}", "Field war", response.IpAddress, response.Port);
                session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
                return;
            }
            session.Send(MigrationPacket.GameToGame(endpoint, response.Token, war.MapId));
            session.State = SessionState.ChangeMap;
        } catch (RpcException ex) {
            session.Send(MigrationPacket.GameToGameError(MigrationError.s_move_err_default));
            session.Send(NoticePacket.Disconnect(new InterfaceText(ex.Message)));
        } finally {
            session.Disconnect();
        }
    }
}

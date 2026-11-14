using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using PlotMode = Maple2.Model.Enum.PlotMode;

namespace Maple2.Server.Game.PacketHandlers;

public class MoveFieldHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.RequestMoveField;

    private enum Command : byte {
        Portal = 0,
        LeaveDungeon = 1,
        VisitHome = 2,
        Return = 3,
        DecorPlanner = 4,
        BlueprintDesigner = 5,
        ModelHome = 6,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.Portal:
                HandlePortal(session, packet);
                return;
            case Command.VisitHome: // s_action_privilege_portal
                HandleVisitHome(session, packet);
                return;
            case Command.LeaveDungeon:
            case Command.Return:
                HandleReturn(session);
                return;
            case Command.DecorPlanner: // s_tutorial_designHome_limit
                HandleDecorPlanner(session);
                return;
            case Command.BlueprintDesigner: // s_tutorial_blueprint_limit
                HandleBlueprintDesigner(session);
                return;
            case Command.ModelHome: // s_meratmarket_ask_move_to_modelhouse
                HandleModelHome(session);
                return;
        }
    }

    private void HandlePortal(GameSession session, IByteReader packet) {
        if (session.Field == null) return;

        int mapId = packet.ReadInt();
        if (mapId != session.Field.MapId) {
            return;
        }

        int portalId = packet.ReadInt();
        packet.ReadInt();
        packet.ReadInt();
        packet.ReadUnicodeString();
        string password = packet.ReadUnicodeString(); // Password for locked portals

        // The client uses only a portal whose box holds the character (PortalRules). A portal that asks first (a confirm
        // or password box) is used when the box is answered, wherever the character has walked since; it counts if the
        // character stood in its box on this field (FieldPortal.Update).
        if (session.Field.TryGetPortal(portalId, out FieldPortal? fieldPortal)
            && !PortalRules.InBox(session.Player.Position, fieldPortal.Position, fieldPortal.Rotation, fieldPortal.Value.Dimension,
                fieldPortal.Value.Offset, InteractRules.ReachSlack)
            && !(fieldPortal.AsksFirst && session.Player.AskedPortalId == portalId)) {
            Logger.Debug("Portal {PortalId} used out of its box", portalId);
            return;
        }

        session.Field.UsePortal(session, portalId, password);
    }

    private void HandleVisitHome(GameSession session, IByteReader packet) {
        packet.ReadInt();
        packet.ReadInt();
        packet.ReadInt();
        long accountId = packet.ReadLong();
        string passcode = packet.ReadUnicodeString();

        switch (HomeVisitRules.Check(session.Player.IsDead, session.Field?.Metadata.Property.HomeReturnable == true,
                    session.Field is HomeFieldManager,
                    session.Field is HomeFieldManager { OwnerId: var ownerId } && ownerId == accountId)) {
            case HomeVisitRules.Refusal.Dead:
                session.Send(EnterUgcMapPacket.Dead());
                return;
            case HomeVisitRules.Refusal.DisabledMap:
                session.Send(EnterUgcMapPacket.DisabledMap());
                return;
            case HomeVisitRules.Refusal.MyHome:
                session.Send(EnterUgcMapPacket.MyHome());
                return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Home? home = db.GetHome(accountId);
        if (home is not { IsHomeSetup: true }) {
            session.Send(EnterUgcMapPacket.CannotVisitThatCharacterHome());
            return;
        }

        if (!HomeEntry.TryEnter(session, home, passcode)) {
            return;
        }

        session.Migrate(home.Indoor.MapId, home.Indoor.OwnerId);
    }

    private void HandleReturn(GameSession session) {
        session.ReturnField();
    }

    private void HandleDecorPlanner(GameSession session) {
        Home home = session.Player.Value.Home;
        if (!home.IsHomeSetup) {
            return;
        }

        session.MigrateToPlanner(PlotMode.DecorPlanner);
    }

    private void HandleBlueprintDesigner(GameSession session) {
        Home home = session.Player.Value.Home;
        if (!home.IsHomeSetup) {
            return;
        }

        session.MigrateToPlanner(PlotMode.BlueprintPlanner);
    }

    private void HandleModelHome(GameSession session) {
        session.MigrateToPlanner(PlotMode.ModelHome);
    }
}

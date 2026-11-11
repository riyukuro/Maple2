using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class HomeActionHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.HomeAction;

    private enum Command : byte {
        Smite = 1,
        Kick = 2,
        PopupMenu3 = 3,
        Survey = 5,
        ChangePortalSettings = 6,
        UpdateBallCoord = 7,
        Quiz = 10,
        ChangeNoticeSettings = 12,
        SendConfigurableSettings = 13,
        PvpControl = 14,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Survey:
                HandleSurvey(session, packet);
                break;
            case Command.UpdateBallCoord:
                HandleUpdateBallCoord(session, packet);
                break;
            case Command.ChangePortalSettings:
                HandleChangePortalSettings(session, packet);
                break;
            case Command.ChangeNoticeSettings:
                HandleChangeNoticeSettings(session, packet);
                break;
            case Command.SendConfigurableSettings:
                HandleConfigurableSettings(session, packet);
                break;
            case Command.Smite:
            case Command.Kick:
            case Command.PopupMenu3:
                // Not handled: the character name picked in the player popup menu.
                Logger.Debug("HomeAction {Command} from {CharacterId}: {Name}", command, session.CharacterId, packet.ReadUnicodeString());
                break;
            case Command.Quiz:
                HandleQuiz(session, packet);
                break;
            case Command.PvpControl:
                // Not handled: a constant 1 and the six values of the PvP control dialog.
                Logger.Debug("HomeAction {Command} from {CharacterId}: {Type} {A} {B} {C} {D} {E} {F}", command, session.CharacterId,
                    packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte());
                break;
        }
    }

    // Not handled: the OX quiz event. Reads what the client writes for each of its sub-types.
    private void HandleQuiz(GameSession session, IByteReader packet) {
        byte type = packet.ReadByte();
        switch (type) {
            case 0: // long quiz id, int, int
                packet.ReadLong();
                packet.ReadInt();
                packet.ReadInt();
                break;
            case 5: // int, int
                packet.ReadInt();
                packet.ReadInt();
                break;
            case 10: // the host's question
                packet.ReadUnicodeString();
                break;
            case 11: // the answer: 1 = O, 0 = X
                packet.ReadByte();
                break;
            // 1, 2, 3, 7, 8, 9, 16, 17, 18: no fields
        }
        Logger.Debug("HomeAction Quiz {Type} from {CharacterId}", type, session.CharacterId);
    }

    private static void HandleSurvey(GameSession session, IByteReader packet) {
        if (session.Field is not HomeFieldManager homeField) {
            return;
        }
        packet.ReadByte();
        packet.ReadLong(); // character id
        long surveyId = packet.ReadLong();
        byte responseIndex = packet.ReadByte();

        string playerName = session.PlayerName;

        HomeSurvey? homeSurvey = homeField.HomeSurvey;
        if (homeSurvey == null || homeSurvey.Id != surveyId) {
            return;
        }

        KeyValuePair<string, List<string>> option = homeSurvey.Options.ElementAtOrDefault(responseIndex);
        if (option.Key == null) {
            return;
        }

        if (!homeSurvey.Started || homeSurvey.Ended || option.Value.Contains(playerName) || !homeSurvey.AvailableCharacters.Contains(playerName)) {
            return;
        }

        homeSurvey.AvailableCharacters.Remove(playerName);
        option.Value.Add(playerName);

        session.Send(HomeActionPacket.SurveyAnswer(playerName));
        homeSurvey.Answers++;

        if (homeSurvey.Answers < homeSurvey.MaxAnswers) {
            return;
        }

        session.Field.Broadcast(HomeActionPacket.SurveyEnd(homeSurvey));
        homeSurvey.End();
    }

    private enum BallUpdateCommand : byte {
        Move = 2,
        Hit = 3,
    };

    private void HandleUpdateBallCoord(GameSession session, IByteReader packet) {
        BallUpdateCommand mode = packet.Read<BallUpdateCommand>();
        int objectId = packet.ReadInt();

        switch (mode) {
            case BallUpdateCommand.Move: {
                // Sent by the ball's owner only: position, linear velocity, angular velocity.
                Vector3 coord = packet.Read<Vector3>();
                Vector3 velocity = packet.Read<Vector3>();
                Vector3 angularVelocity = packet.Read<Vector3>();

                FieldGuideObject? guideObject = session.GuideObject;
                if (guideObject?.Value is not BallGuideObject || guideObject.ObjectId != objectId) {
                    Logger.Warning("Ball not found");
                    return;
                }

                guideObject.Position = coord;
                session.Field?.Broadcast(HomeActionPacket.UpdateBall(guideObject, velocity, angularVelocity), session);
                break;
            }
            case BallUpdateCommand.Hit: {
                // Sent by any player who touches a ball: force, then the direction from the ball to that player.
                // Only the ball's owner applies it, so the ball is looked up among all players.
                Vector3 force = packet.Read<Vector3>();
                Vector3 direction = packet.Read<Vector3>();
                if (session.Field is null) {
                    return;
                }
                if (!float.IsFinite(force.LengthSquared()) || !float.IsFinite(direction.LengthSquared())) {
                    return;
                }

                foreach (FieldPlayer fieldPlayer in session.Field.Players.Values) {
                    FieldGuideObject? ball = fieldPlayer.Session.GuideObject;
                    if (ball?.Value is BallGuideObject && ball.ObjectId == objectId) {
                        fieldPlayer.Session.Send(HomeActionPacket.HitBall(ball, force, direction));
                        return;
                    }
                }
                break;
            }
        }
    }

    private void HandleChangePortalSettings(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        packet.ReadByte();
        Vector3B coord = packet.Read<Vector3B>();

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot == null) {
            return;
        }

        if (!plot.Cubes.TryGetValue(coord, out PlotCube? cube)) {
            Logger.Warning("Cube not found at {0}", coord);
            return;
        }

        if (cube.Interact?.PortalSettings is null) {
            Logger.Warning("Cube does not have portal settings at {0}", coord);
            return;
        }

        cube.Interact.PortalSettings.PortalName = packet.ReadUnicodeString();
        cube.Interact.PortalSettings.Method = (PortalActionType) packet.ReadByte();
        cube.Interact.PortalSettings.Destination = (CubePortalDestination) packet.ReadByte();
        cube.Interact.PortalSettings.DestinationTarget = packet.ReadUnicodeString();

        foreach (FieldPortal portal in session.Field.GetPortals()) {
            session.Field.RemovePortal(portal.ObjectId);
        }

        List<PlotCube> cubePortals = plot.Cubes.Values
            .Where(x => x.ItemId is Constant.InteriorPortalCubeId && x.Interact?.PortalSettings is not null)
            .ToList();

        foreach (PlotCube cubePortal in cubePortals) {
            FieldPortal? fieldPortal = session.Field.SpawnCubePortal(cubePortal);
            if (fieldPortal == null) {
                continue;
            }
            session.Field.Broadcast(PortalPacket.Add(fieldPortal));
        }

        session.Housing.SaveHome();
    }

    private void HandleChangeNoticeSettings(GameSession session, IByteReader packet) {
        bool editing = packet.ReadBool();
        Vector3B coord = packet.Read<Vector3B>();

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot == null) {
            return;
        }

        if (!plot.Cubes.TryGetValue(coord, out PlotCube? cube)) {
            Logger.Warning("Cube not found at {0}", coord);
            return;
        }

        if (cube.Interact?.NoticeSettings is null) {
            Logger.Warning("Cube does not have notice settings at {0}", coord);
            return;
        }

        cube.Interact.NoticeSettings.Notice = packet.ReadUnicodeString();
        cube.Interact.NoticeSettings.Distance = packet.ReadByte();

        session.Field?.Broadcast(HomeActionPacket.SendCubeNoticeSettings(cube, editing: false));
        session.Housing.SaveHome();
    }

    private void HandleConfigurableSettings(GameSession session, IByteReader packet) {
        var coord = packet.Read<Vector3B>();

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot == null) {
            return;
        }

        if (!plot.Cubes.TryGetValue(coord, out PlotCube? cube)) {
            Logger.Warning("Cube not found at {0}", coord);
            return;
        }

        if (cube.Interact?.PortalSettings is not null) {
            List<string> otherPortalsNames = plot.Cubes.Values
                .Where(x => x.ItemId is Constant.InteriorPortalCubeId && x.Id != cube.Id)
                .Select(x => x.Interact!.PortalSettings?.PortalName ?? string.Empty)
                .ToList();

            session.Send(HomeActionPacket.SendCubePortalSettings(cube, otherPortalsNames));
        } else if (cube.Interact?.NoticeSettings is not null) {
            session.Send(HomeActionPacket.SendCubeNoticeSettings(cube, editing: true));
        } else {
            Logger.Warning("Cube is not configurable at {0}", coord);
        }
    }
}

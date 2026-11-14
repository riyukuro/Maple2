using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public partial class HomeActionHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.HomeAction;

    private enum Command : byte {
        Smite = 1,
        Kick = 2,
        Revive = 3,
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
            case Command.Revive:
                HandleVisitor(session, command, packet);
                break;
            case Command.Quiz:
                HandleQuiz(session, packet);
                break;
            case Command.PvpControl:
                // UIUGCPVPControlDialog "OnStart" (uiugcpvpcontroldialog.cpp:109): a constant 1 and the six values
                // of the PvP control dialog. The server has no PvP mode for homes; the client reads only the six
                // values back (HomeAction 14, 0) and has no refusal text. Read and logged. The PVP function cubes of
                // items 50400174 to 50400176 carry a team (0, 1, 2); the client has no match rule and names no flag.
                Logger.Debug("HomeAction {Command} from {CharacterId}: {Type} {A} {B} {C} {D} {E} {F}", command, session.CharacterId,
                    packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte());
                break;
        }
    }

    // The owner's entries in the player menu: 1 on a living visitor, 3 on a dead one, 2 on either.
    private static void HandleVisitor(GameSession session, Command command, IByteReader packet) {
        string name = packet.ReadUnicodeString();
        if (session.Field is not HomeFieldManager home) {
            return;
        }

        bool found = home.TryGetPlayer(name, out FieldPlayer? target);
        switch (HomeVisitorRules.Pick((byte) command, home.OwnerId == session.AccountId, found,
                    target?.Value.Character.Id == session.CharacterId, target?.IsDead ?? false)) {
            case HomeVisitorRules.Action.Kill:
                target!.ConsumeHp((int) Math.Min(int.MaxValue, target.Stats.Values[BasicAttribute.Health].Current));
                break;
            case HomeVisitorRules.Action.Revive:
                if (target!.Revive(true)) {
                    target.Tombstone = null;
                }
                break;
            case HomeVisitorRules.Action.Kick:
                target!.Session.ReturnField();
                break;
        }
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
        (Vector3B coord, string name, byte method, byte destination, string target) = HomeEditRules.ReadPortal(packet);

        // The owner's dialog only: a visitor's own home has the same map id and plot number.
        Plot? plot = session.Housing.GetOwnFieldPlot();
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

        IEnumerable<string> otherNames = plot.Cubes.Values
            .Where(x => x.Id != cube.Id && x.Interact?.PortalSettings is not null)
            .Select(x => x.Interact!.PortalSettings!.PortalName);
        if (!HomeEditRules.PortalAllowed(name, method, destination, target, otherNames)) {
            return;
        }

        cube.Interact.PortalSettings.PortalName = name;
        cube.Interact.PortalSettings.Method = (PortalActionType) method;
        cube.Interact.PortalSettings.Destination = (CubePortalDestination) destination;
        cube.Interact.PortalSettings.DestinationTarget = target;

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
        (Vector3B coord, string notice, byte distance) = HomeEditRules.ReadNotice(packet);

        Plot? plot = session.Housing.GetOwnFieldPlot();
        if (plot == null || !HomeEditRules.NoticeAllowed(notice)) {
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

        cube.Interact.NoticeSettings.Notice = notice;
        cube.Interact.NoticeSettings.Distance = distance;

        session.Field?.Broadcast(HomeActionPacket.SendCubeNoticeSettings(cube, editing: false));
        session.Housing.SaveHome();
    }

    private void HandleConfigurableSettings(GameSession session, IByteReader packet) {
        var coord = packet.Read<Vector3B>();

        Plot? plot = session.Housing.GetOwnFieldPlot();
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

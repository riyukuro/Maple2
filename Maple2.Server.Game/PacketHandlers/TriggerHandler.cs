using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class TriggerHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Trigger;

    private enum Command : byte {
        NextTrigger = 4,
        SkipCutscene = 8,
        Ui = 9,
        LoadScript = 11,
        SaveScript = 13,
        DiscardScript = 22,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.NextTrigger:
                HandleNextTrigger(session);
                return;
            case Command.SkipCutscene:
                HandleSkipCutscene(session);
                return;
            case Command.Ui:
                HandleUpdateWidget(session, packet);
                return;
            case Command.LoadScript:
                HandleLoadScript(session, packet);
                return;
            case Command.SaveScript:
                HandleSaveScript(session, packet);
                return;
            case Command.DiscardScript:
                HandleDiscardScript(session, packet);
                return;
        }
    }

    // The "Next Trigger" key (key 300014, keytable.cpp:778), bound by default for every player: GMs only, and a
    // press from anyone else is dropped with a Debug line, not the GM-packet warning. Each trigger of the field
    // moves on at its next update (FieldTrigger.RequestNext).
    private void HandleNextTrigger(GameSession session) {
        if (session.Field == null || session.Player == null || !GmCheck.IsGm(session.Player.AdminPermissions)) {
            Logger.Debug("Trigger NextTrigger from {CharacterId} dropped (not a GM)", session.CharacterId);
            return;
        }

        int count = 0;
        foreach (FieldTrigger trigger in session.Field.EnumerateTrigger()) {
            trigger.RequestNext();
            count++;
        }
        Logger.Information("Trigger NextTrigger from GM {CharacterId} on map {MapId}: {Count} triggers", session.CharacterId, session.Field.MapId, count);
    }

    private void HandleSkipCutscene(GameSession session) {
        if (session.Field == null) {
            return;
        }

        foreach (FieldTrigger trigger in session.Field.EnumerateTrigger()) {
            if (trigger.Skip()) {
                return;
            }
        }
    }

    private void HandleUpdateWidget(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        var widgetType = packet.Read<WidgetType>();
        int arg = packet.ReadInt();
        switch (widgetType) {
            case WidgetType.Guide:
                if (session.Field.Widgets.TryGetValue(widgetType.ToString(), out Widget? guideWidget)) {
                    guideWidget.Conditions["IsTriggerEvent"] = arg;
                }
                break;
            case WidgetType.SceneMovie:
                if (session.Field.Widgets.TryGetValue(widgetType.ToString(), out Widget? sceneMovieWidget)) {
                    sceneMovieWidget.Conditions["IsStop"] = arg;
                    session.Send(TriggerPacket.UiSkipMovie(arg));
                }
                break;
            case WidgetType.TypingGame:
                if (session.Field.Widgets.TryGetValue(widgetType.ToString(), out Widget? typingWidget) && typingWidget is TypingGameWidget typingGame) {
                    typingGame.State.Reply(arg);
                }
                break;
            default:
                Logger.Warning("Unhandled widget type: {WidgetType}", widgetType);
                break;
        }
    }

    private void HandleLoadScript(GameSession session, IByteReader packet) {
        int cubeId = packet.ReadInt();
    }

    private void HandleSaveScript(GameSession session, IByteReader packet) {
        int cubeId = packet.ReadInt();
        string xml = packet.ReadString();
    }

    private void HandleDiscardScript(GameSession session, IByteReader packet) {
        int cubeId = packet.ReadInt();
    }
}

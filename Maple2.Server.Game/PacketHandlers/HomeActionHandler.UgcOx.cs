using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The home OX quiz event (HomeAction 10). Client writers: 1, 3 and 5 in unit_140dfef30_ugcoxevent.cpp (FUN_140dff2b0,
// FUN_140dff210, FUN_140dff580), 0 uiquizoxcreatedialog.cpp (OnCreate), 7 uiquizoxwelcomedialog.cpp:97, 10
// uiquizhostdialog.cpp:136, 11 uiquizoxselectdialog.cpp:80. The host makes the event in its own home (2, 0, 3), a
// portal there leads into the stadium room (5, then RequestMoveField), and the host runs the game from the stadium
// (7 to 18), where the stadium script reads the event through the OxQuizUGC widget.
public partial class HomeActionHandler {
    private enum QuizCommand : byte {
        Settings = 0,
        Promote = 1,
        Open = 2,
        Create = 3,
        Enter = 5,
        Start = 7,
        Finish = 8,
        Leave = 9,
        Question = 10,
        Answer = 11,
        Judge = 16,
        RemoveWall = 17,
        Resurrect = 18,
    }

    // The stadium's boxes: the fallen wait in 104; a resurrect puts them back on the stage, box 105.
    private const int FallenBoxId = 104;
    private const int StageBoxId = 105;

    private void HandleQuiz(GameSession session, IByteReader packet) {
        var command = (QuizCommand) packet.ReadByte();
        switch (command) {
            case QuizCommand.Open:
                OpenUgcOx(session);
                return;
            case QuizCommand.Settings:
                HandleQuizSettings(session, UgcOxRules.ReadSettings(packet));
                return;
            case QuizCommand.Create:
                HandleQuizCreate(session);
                return;
            case QuizCommand.Enter:
                // The two ints are the portal's own values in the client; the server answers from its event.
                packet.ReadInt();
                packet.ReadInt();
                HandleQuizEnter(session);
                return;
            case QuizCommand.Promote:
                HandleQuizPromote(session);
                return;
        }

        // The rest come from the host's dialogs in the stadium.
        if (session.Field?.UgcOx is not { } ox || ox.HostCharacterId != session.CharacterId) {
            Logger.Debug("HomeAction Quiz {Command} from {CharacterId} outside its stadium", command, session.CharacterId);
            return;
        }
        switch (command) {
            case QuizCommand.Start:
                if (!ox.Start()) {
                    Logger.Information("OX quiz event of {HostCharacterId} canceled at the start: {Count} entrants",
                        ox.HostCharacterId, ox.EntrantCount);
                }
                break;
            case QuizCommand.Finish:
                if (ox.Finish() == UgcOxFinish.TooEarly) {
                    session.Send(NoticePacket.Message("s_ugcox_finish_fail_by_round"));
                }
                break;
            case QuizCommand.Leave:
                // The host's leave is a pause (s_ugcox_leave_warning_host): it comes back through the home portal.
                Logger.Debug("OX quiz host {CharacterId} left the stadium", session.CharacterId);
                break;
            case QuizCommand.Question:
                ox.SubmitQuestion(packet.ReadUnicodeString());
                break;
            case QuizCommand.Answer:
                ox.SubmitAnswer(packet.ReadByte() == 1);
                break;
            case QuizCommand.Judge:
                // The script judges by itself (U3): the client's judge button has nothing to set.
                break;
            case QuizCommand.RemoveWall:
                ox.RemoveWall();
                break;
            case QuizCommand.Resurrect:
                ResurrectFallen(session.Field);
                break;
            default:
                Logger.Debug("HomeAction Quiz {Command} from {CharacterId}", command, session.CharacterId);
                break;
        }
    }

    // c2s 2 and the host ticket's use (item function OpenUGCEvent): the create dialog with its fees, or the refusal.
    public static void OpenUgcOx(GameSession session) {
        if (CreateRefusal(session) is { } refusal) {
            session.Send(NoticePacket.Message(refusal));
            return;
        }
        session.Send(HomeActionPacket.UgcOxFees(UgcOxRules.Fees));
    }

    private static string? CreateRefusal(GameSession session) {
        bool inOwnHome = session.Field is HomeFieldManager home && home.OwnerId == session.AccountId;
        bool designing = session.Housing.GetFieldPlot()?.IsPlanner == true;
        bool holdsTicket = session.Item.Inventory.Find(UgcOxRules.TicketItemId).Sum(item => item.Amount) >= 1;
        bool hasEvent = session.FieldFactory.FindUgcOx(session.AccountId) != null;
        return UgcOxRules.CreateRefusal(inOwnHome, designing, holdsTicket, hasEvent);
    }

    // c2s 0: the create dialog's settings; the client asks for the confirm box.
    private void HandleQuizSettings(GameSession session, UgcOxRules.Settings settings) {
        if (!UgcOxRules.Valid(settings)) {
            Logger.Warning("HomeAction Quiz settings from {CharacterId} not in the dialog: fee {Fee}, max {Max}, share {Share}",
                session.CharacterId, settings.Fee, settings.Max, settings.Share);
            return;
        }
        if (CreateRefusal(session) is { } refusal) {
            session.Send(NoticePacket.Message(refusal));
            return;
        }
        session.UgcOxPending = settings;
        session.Send(HomeActionPacket.UgcOxConfirm(session.PlayerName, settings.Fee, settings.Max, settings.Share));
    }

    // c2s 3: the confirm box's yes. The stadium room and the home portal are made; the host moves in at once.
    private void HandleQuizCreate(GameSession session) {
        if (session.UgcOxPending is not { } settings) {
            return;
        }
        session.UgcOxPending = null;
        if (CreateRefusal(session) is { } refusal) {
            session.Send(NoticePacket.Message(refusal));
            return;
        }
        var home = (HomeFieldManager) session.Field!;
        var ox = new UgcOxEvent(session.AccountId, session.CharacterId, session.PlayerName, home.MapId, settings);
        if (session.FieldFactory.CreateUgcOxStadium(ox) == null) {
            Logger.Error("OX quiz stadium of {CharacterId} could not be made", session.CharacterId);
            return;
        }
        // The event takes one host ticket (s_ugcox_create_fail_item_lack names it as what the event needs).
        Item? ticket = session.Item.Inventory.Find(UgcOxRules.TicketItemId).FirstOrDefault();
        if (ticket == null || !session.Item.Inventory.Consume(ticket.Uid, 1)) {
            session.FieldFactory.RemoveUgcOx(ox);
            session.Send(NoticePacket.Message("s_ugcox_create_fail_item_lack"));
            return;
        }
        FieldPortal portal = home.SpawnUgcOxPortal(session.Player);
        ox.HomePortalId = portal.Value.Id;
        ox.HomePortalObjectId = portal.ObjectId;
        Logger.Information("OX quiz event of {CharacterId} made: fee {Fee}, max {Max}, share {Share}, room {RoomId}",
            session.CharacterId, settings.Fee, settings.Max, settings.Share, ox.StadiumRoomId);

        long now = Environment.TickCount64;
        session.Send(HomeActionPacket.UgcOxCreated(settings.Fee, ox.PromoteCount, ox.PromoteCooldownLeft(now),
            UgcOxRules.PromoteTotal, UgcOxRules.PromoteCooldownMs));
        session.Send(MoveBox(ox));
        home.Broadcast(NoticePacket.Message(new InterfaceText(StringCode.s_ugcox_create_portal, ox.HostName)));
    }

    // c2s 5: a use of the home's stadium portal; the client then moves (the host) or shows the fee box (anyone else).
    private static void HandleQuizEnter(GameSession session) {
        if (session.Field is not HomeFieldManager home || session.FieldFactory.FindUgcOx(home.OwnerId) is not { } ox) {
            return;
        }
        session.Send(MoveBox(ox));
    }

    private static ByteWriter MoveBox(UgcOxEvent ox) {
        return HomeActionPacket.UgcOxMove(ox.HomeMapId, ox.HomePortalId, ox.HostAccountId, ox.HostName,
            ox.Settings.Fee, ox.Settings.Max, ox.Settings.Share);
    }

    // c2s 1: the host's promote line, a chat line of kind 22 (UgcEvent) with the join link. TextToHtml splits it on
    // ",": the shown text, two unused values, then the fee, the most entrants and the share. The line goes to this
    // channel only: the event lives on the host's channel (FORK.md).
    private static void HandleQuizPromote(GameSession session) {
        if (session.FieldFactory.FindUgcOx(session.AccountId) is not { } ox) {
            return;
        }
        long now = Environment.TickCount64;
        if (ox.Promote(now) == UgcOxPromote.Ok) {
            // Chosen here: the shown text names the host's event.
            string text = $"{ox.HostName}'s OX Quiz,0,0,{ox.Settings.Fee},{ox.Settings.Max},{ox.Settings.Share}";
            session.BroadcastChannel(ChatPacket.Message(session.AccountId, session.CharacterId, session.PlayerName, ChatType.UgcEvent, text));
        }
        session.Send(HomeActionPacket.UgcOxPromote(ox.PromoteCount, ox.PromoteCooldownLeft(now), UgcOxRules.PromoteTotal,
            UgcOxRules.PromoteCooldownMs));
    }

    // c2s 18: the fallen (box 104) go back onto the stage (box 105), on top of the box.
    private static void ResurrectFallen(FieldManager field) {
        if (!field.TriggerObjects.Boxes.TryGetValue(FallenBoxId, out TriggerBox? fallen)
            || !field.TriggerObjects.Boxes.TryGetValue(StageBoxId, out TriggerBox? stage)) {
            return;
        }
        Vector3 spot = stage.Metadata.Position + new Vector3(0, 0, stage.Metadata.Dimensions.Z / 2);
        foreach (FieldPlayer player in field.Players.Values) {
            if (fallen.Contains(player.Position)) {
                player.MoveToPosition(spot, player.Rotation);
            }
        }
    }
}

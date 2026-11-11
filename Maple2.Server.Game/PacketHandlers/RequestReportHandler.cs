using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class RequestReportHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestReport;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.ReportCooldown > 0 && session.ReportCooldown.FromEpochSeconds().AddMinutes(1) > DateTime.Now) {
            return;
        }

        // Client: UIReportDialog::SendReport (uireportdialog.cpp:348-399) sends a text of 1 to 4000 letters.
        (string playerName, string reason, byte kind, int flags) = ReportRules.ReadHead(packet);
        if (!ReportRules.TextValid(reason)) {
            return;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(playerName);
        if (characterId == 0) {
            Logger.Debug("Report: no character of the name sent by {CharacterId}", session.CharacterId);
            return;
        }
        var reportCategory = (ReportCategory) kind;
        switch (reportCategory) {
            case ReportCategory.Player:
                var playerFlag = (PlayerReportFlag) flags;
                db.ReportPlayer(characterId, playerName, session.CharacterId, session.PlayerName, reason, playerFlag);
                break;
            case ReportCategory.Chat:
                var chatFlag = (ChatReportFlag) flags;
                string chatMessage = packet.ReadUnicodeString();
                if (!ReportRules.AttachedValid(chatMessage)) {
                    return;
                }
                db.ReportChat(characterId, playerName, session.CharacterId, session.PlayerName, reason, chatMessage, chatFlag);
                break;
            case ReportCategory.Poster:
                var posterFlag = (PosterReportFlag) flags;
                // The client sends the poster id as a long (UIReportDialog::SendReport).
                long posterId = packet.ReadLong();
                string templateId = packet.ReadUnicodeString();
                if (!ReportRules.AttachedValid(templateId)) {
                    return;
                }
                db.ReportPoster(characterId, playerName, session.CharacterId, session.PlayerName, reason, posterId, templateId, posterFlag);
                break;
            case ReportCategory.ItemDesign:
                var designItemFlag = (DesignItemReportFlag) flags;
                long listingId = packet.ReadLong();
                db.ReportDesignItem(characterId, playerName, session.CharacterId, session.PlayerName, reason, listingId, designItemFlag);
                break;
            case ReportCategory.Home:
                var homeFlag = (HomeReportFlag) flags;
                long homeId = packet.ReadLong();
                int mapId = packet.ReadInt();
                int plotId = packet.ReadInt();
                db.ReportHome(characterId, playerName, session.CharacterId, session.PlayerName, reason, homeId, mapId, plotId, homeFlag);
                break;
            case ReportCategory.Pet:
                var petFlag = (PetReportFlag) flags;
                string petName = packet.ReadUnicodeString();
                if (!ReportRules.AttachedValid(petName)) {
                    return;
                }
                db.ReportPet(characterId, playerName, session.CharacterId, session.PlayerName, reason, petName, petFlag);
                break;
            default:
                Logger.Debug("Report: kind {Kind} from {CharacterId} not handled", kind, session.CharacterId);
                return;
        }

        session.ReportCooldown = DateTime.Now.ToEpochSeconds();
    }
}

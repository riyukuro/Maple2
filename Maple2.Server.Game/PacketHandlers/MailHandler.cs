using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class MailHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Mail;

    private enum Command : byte {
        Load = 0,
        Send = 1,
        Read = 2,
        Collect = 11,
        AdBill = 12, // ASMailAdBillData
        Delete = 13,
        BulkRead = 18,
        BulkCollect = 19,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Load:
                HandleLoad(session);
                return;
            case Command.Send:
                HandleSend(session, packet);
                return;
            case Command.Read:
                HandleRead(session, packet);
                return;
            case Command.Collect:
                HandleCollect(session, packet);
                return;
            case Command.AdBill:
                HandleAdBill(session, packet);
                return;
            case Command.Delete:
                HandleDelete(session, packet);
                return;
            case Command.BulkRead:
                HandleBulkRead(session, packet);
                return;
            case Command.BulkCollect:
                HandleBulkCollect(session, packet);
                return;
        }
    }

    private static void HandleLoad(GameSession session) {
        session.Mail.Load();
    }

    private static void HandleSend(GameSession session, IByteReader packet) {
        string receiverName = packet.ReadUnicodeString();
        string title = packet.ReadUnicodeString();
        string content = packet.ReadUnicodeString();

        if (!MailRules.CanSend(title, content)) {
            session.Send(MailPacket.Error(MailError.s_mail_error_sendmail, (byte) Command.Send));
            return;
        }

        // Names are looked up without case (GetCharacterId), so the own name in another case is the sender too.
        if (string.Equals(receiverName, session.PlayerName, StringComparison.OrdinalIgnoreCase)) {
            session.Send(MailPacket.Error(MailError.s_mail_error_recipient_equal_sender, (byte) Command.Send));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long receiverId = db.GetCharacterId(receiverName);
        if (receiverId == default) {
            session.Send(MailPacket.Error(MailError.s_mail_error_username, (byte) Command.Send));
            return;
        }
        if (receiverId == session.CharacterId) {
            session.Send(MailPacket.Error(MailError.s_mail_error_recipient_equal_sender, (byte) Command.Send));
            return;
        }
        // The client has a text for both directions of a block (OnMail error 27 and 28).
        if (session.Buddy.IsBlocked(receiverId)) {
            session.Send(MailPacket.Error(MailError.s_mail_error_block_from_me, (byte) Command.Send));
            return;
        }
        if (db.GetBuddyType(receiverId, session.CharacterId) == BuddyType.Blocked) {
            session.Send(MailPacket.Error(MailError.s_mail_error_block_from_other, (byte) Command.Send));
            return;
        }

        var mail = new Mail {
            SenderId = session.CharacterId,
            ReceiverId = receiverId,
            Type = MailType.Player,
            SenderName = session.PlayerName,
            Title = title,     // TODO: xml escaping?
            Content = content, // TODO: xml escaping?
        };

        mail = db.CreateMail(mail);
        if (mail == null) {
            session.Send(MailPacket.Error(MailError.s_mail_error_createmail, (byte) Command.Send));
            return;
        }

        session.ConditionUpdate(ConditionType.send_mail);
        session.Send(MailPacket.Send());
        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = receiverId,
                MailId = mail.Id,
            });
        } catch { /* ignored */ }
    }

    private static void HandleRead(GameSession session, IByteReader packet) {
        long mailId = packet.ReadLong();

        MailError error = session.Mail.Read(mailId);
        if (error != MailError.none) {
            session.Send(MailPacket.Error(error, (byte) Command.Read));
        }
    }

    private static void HandleCollect(GameSession session, IByteReader packet) {
        long mailId = packet.ReadLong();

        MailError error = session.Mail.Collect(mailId);
        if (error != MailError.none) {
            session.Send(MailPacket.Error(error, (byte) Command.Collect));
        }
    }

    private static void HandleAdBill(GameSession session, IByteReader packet) {
        long mailId = packet.ReadLong();
    }

    private static void HandleDelete(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();

        for (int i = 0; i < count; i++) {
            long mailId = packet.ReadLong();

            session.Mail.Delete(mailId);
        }
    }

    private static void HandleBulkRead(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();

        for (int i = 0; i < count; i++) {
            long mailId = packet.ReadLong();

            MailError error = session.Mail.Read(mailId);
            if (error != MailError.none && !MailRules.BulkSkips(error)) {
                session.Send(MailPacket.Error(error, (byte) Command.BulkRead));
                return;
            }
        }
    }

    private static void HandleBulkCollect(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();

        for (int i = 0; i < count; i++) {
            long mailId = packet.ReadLong();

            MailError error = session.Mail.Collect(mailId);
            if (error != MailError.none && !MailRules.BulkSkips(error)) {
                session.Send(MailPacket.Error(error, (byte) Command.BulkCollect));
                return;
            }
        }
    }
}

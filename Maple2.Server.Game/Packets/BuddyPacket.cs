using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class BuddyPacket {
    private enum Command : byte {
        Load = 1,
        Invite = 2,
        Accept = 3,
        Decline = 4,
        Block = 5,
        Unblock = 6,
        Remove = 7,
        UpdateInfo = 8,
        Append = 9,
        UpdateBlock = 10,
        NotifyAccept = 11,
        NotifyBlock = 12,
        NotifyRemove = 13,
        NotifyOnline = 14,
        StartList = 15,
        Cancel = 17,
        Forbidden = 18,
        EndList = 19,
        AskAutoReject = 20,
    }

    public static ByteWriter Load(ICollection<Buddy> buddies, ICollection<Buddy> blocked) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(buddies.Count + blocked.Count);
        foreach (Buddy buddy in buddies) {
            pWriter.WriteClass<Buddy>(buddy);
        }
        foreach (Buddy buddy in blocked) {
            pWriter.WriteClass<Buddy>(buddy);
        }

        return pWriter;
    }

    public static ByteWriter Invite(string name = "", string message = "", BuddyError error = BuddyError.ok) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Invite);
        pWriter.Write<BuddyError>(error);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    public static ByteWriter Accept(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Accept);
        pWriter.Write<BuddyError>(BuddyError.ok);
        pWriter.WriteLong(buddy.Id);
        pWriter.WriteLong(buddy.Info.CharacterId);
        pWriter.WriteLong(buddy.Info.AccountId);
        pWriter.WriteUnicodeString(buddy.Info.Name);

        return pWriter;
    }

    // A refused accept, decline, unblock, remove or cancel: the same layout with the error and the entry id. The client
    // acts on these protocols only for result 0 (buddymanager.cpp:131-178), so a refusal changes nothing there.
    public static ByteWriter Accept(long entryId, BuddyError error) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Accept);
        pWriter.Write<BuddyError>(error);
        pWriter.WriteLong(entryId);
        pWriter.WriteLong(0);
        pWriter.WriteLong(0);
        pWriter.WriteUnicodeString("");

        return pWriter;
    }

    public static ByteWriter Decline(long entryId, BuddyError error) => Refused(Command.Decline, entryId, error);
    public static ByteWriter Unblock(long entryId, BuddyError error) => Refused(Command.Unblock, entryId, error);
    public static ByteWriter Remove(long entryId, BuddyError error) => Refused(Command.Remove, entryId, error);
    public static ByteWriter Cancel(long entryId, BuddyError error) => Refused(Command.Cancel, entryId, error);

    private static ByteWriter Refused(Command command, long entryId, BuddyError error) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(command);
        pWriter.Write<BuddyError>(error);
        pWriter.WriteLong(entryId);
        if (command == Command.Remove) {
            pWriter.WriteLong(0);
            pWriter.WriteLong(0);
            pWriter.WriteUnicodeString("");
        }

        return pWriter;
    }

    public static ByteWriter Decline(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Decline);
        pWriter.Write<BuddyError>(BuddyError.ok);
        pWriter.WriteLong(buddy.Id);

        return pWriter;
    }

    public static ByteWriter Block(long entryId = 0, string name = "", string message = "", BuddyError error = BuddyError.ok) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Block);
        pWriter.Write<BuddyError>(error);
        pWriter.WriteLong(entryId);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    public static ByteWriter Unblock(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Unblock);
        pWriter.Write<BuddyError>(BuddyError.ok);
        pWriter.WriteLong(buddy.Id);

        return pWriter;
    }

    public static ByteWriter Remove(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Remove);
        pWriter.Write<BuddyError>(BuddyError.ok);
        pWriter.WriteLong(buddy.Id);
        pWriter.WriteLong(buddy.Info.CharacterId);
        pWriter.WriteLong(buddy.Info.AccountId);
        pWriter.WriteUnicodeString(buddy.Info.Name);

        return pWriter;
    }

    public static ByteWriter UpdateInfo(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.UpdateInfo);
        pWriter.WriteClass<Buddy>(buddy);

        return pWriter;
    }

    public static ByteWriter Append(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Append);
        pWriter.WriteClass<Buddy>(buddy);

        return pWriter;
    }

    public static ByteWriter UpdateBlock(long entryId = 0, string name = "", string message = "", BuddyError error = BuddyError.ok) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.UpdateBlock);
        pWriter.Write<BuddyError>(error);
        pWriter.WriteLong(entryId);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    public static ByteWriter NotifyAccept(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.NotifyAccept);
        pWriter.WriteLong(buddy.Id);

        return pWriter;
    }

    public static ByteWriter NotifyBlock(string name, BuddyError error = BuddyError.ok) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.NotifyBlock);
        pWriter.Write<BuddyError>(error);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // The friend notices kept while the player was offline. The client shows each as a notice line: "approved" also
    // marks the friend new in the list (so it is sent after the list), "declined" and "removed" name the other player.
    public static ByteWriter Notices(ICollection<BuddyNotice> notices) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.NotifyRemove);
        pWriter.WriteInt(notices.Count);
        foreach (BuddyNotice notice in notices) {
            pWriter.WriteUnicodeString(notice.Name);
            pWriter.WriteUnicodeString(NoticeAction(notice.Type));
            pWriter.WriteLong(notice.EntryId);
        }

        return pWriter;
    }

    public static string NoticeAction(BuddyNoticeType type) {
        return type switch {
            BuddyNoticeType.Approved => "approved",
            BuddyNoticeType.Declined => "declined",
            BuddyNoticeType.Removed => "removed",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public static ByteWriter NotifyOnline(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.NotifyOnline);
        pWriter.WriteBool(!buddy.Info.Online); // true == offline
        pWriter.WriteLong(buddy.Id);
        pWriter.WriteUnicodeString(buddy.Info.Name);

        return pWriter;
    }

    public static ByteWriter StartList() {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.StartList);

        return pWriter;
    }

    public static ByteWriter Cancel(Buddy buddy) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Cancel);
        pWriter.Write<BuddyError>(BuddyError.ok);
        pWriter.WriteLong(buddy.Id);

        return pWriter;
    }

    // s_ban_check_err_any: Contains a forbidden word.
    public static ByteWriter Forbidden() {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.Forbidden);

        return pWriter;
    }

    public static ByteWriter EndList(int count) {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.Write<Command>(Command.EndList);
        pWriter.WriteInt(count);

        return pWriter;
    }

    // No body. The client answers with Buddy sub-type 20 and its "refuse friend requests" option.
    public static ByteWriter AskAutoReject() {
        var pWriter = Packet.Of(SendOp.Buddy);
        pWriter.WriteByte((byte) Command.AskAutoReject);

        return pWriter;
    }
}

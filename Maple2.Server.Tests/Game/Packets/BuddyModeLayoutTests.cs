using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The Buddy modes other than the entry lists, read as CBuddyManager::OnBuddy (buddymanager.cpp:95) with its response
// decoder Unk_SPDB::ReadFrom (serverpacketdef.cpp:115) reads them: 2 byte result, name, message; 3 and 7 byte, int64
// entry id, int64, int64, name; 4, 6 and 17 byte, int64 id; 5 and 10 byte, int64, name, message; 11 int64; 12 byte,
// name; 14 byte offline flag, int64, name; 13 int count then name, action string and int64 per notice; 15, 18 and 20
// read nothing (19, the end of the list, sends a count the client does not read).
public class BuddyModeLayoutTests {
    private static Buddy Make(long id) {
        var info = new PlayerInfo(new CharacterInfo(3, 4, "bud", "m", "", Gender.Male, Job.Newbie, 42), "home", new AchievementInfo(), []);
        return new Buddy(new BuddyEntry { Id = id, OwnerId = 1, BuddyId = 4, LastModified = 99, Type = BuddyType.Default }, info);
    }

    private static ByteReader Open(ByteWriter writer, byte command) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Buddy));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        return reader;
    }

    [Test]
    public void InviteBlockAndUpdateBlock_AreResultIdNameMessage() {
        ByteReader invite = Open(BuddyPacket.Invite("Nm", "Msg", BuddyError.s_buddy_err_miss_id), 2);
        Assert.That(invite.ReadByte(), Is.EqualTo((byte) BuddyError.s_buddy_err_miss_id));
        Assert.That(invite.ReadUnicodeString(), Is.EqualTo("Nm"));
        Assert.That(invite.ReadUnicodeString(), Is.EqualTo("Msg"));
        Assert.That(invite.Available, Is.Zero);

        foreach ((ByteWriter writer, byte mode) in new[] {
                     (BuddyPacket.Block(7, "Nm", "Msg"), (byte) 5),
                     (BuddyPacket.UpdateBlock(7, "Nm", "Msg"), (byte) 10),
                 }) {
            ByteReader reader = Open(writer, mode);
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadLong(), Is.EqualTo(7));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Nm"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Msg"));
            Assert.That(reader.Available, Is.Zero);
        }
    }

    [Test]
    public void AcceptAndRemove_CarryTheEntryAndTheBuddy() {
        Buddy buddy = Make(7);
        foreach ((ByteWriter writer, byte mode) in new[] {
                     (BuddyPacket.Accept(buddy), (byte) 3),
                     (BuddyPacket.Remove(buddy), (byte) 7),
                 }) {
            ByteReader reader = Open(writer, mode);
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadLong(), Is.EqualTo(7));
            Assert.That(reader.ReadLong(), Is.EqualTo(4)); // character id
            Assert.That(reader.ReadLong(), Is.EqualTo(3)); // account id
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("bud"));
            Assert.That(reader.Available, Is.Zero);
        }

        // A refused accept and remove keep the same shape (the client acts on result 0 only).
        ByteReader refusedAccept = Open(BuddyPacket.Accept(7, BuddyError.s_buddy_err_miss_id), 3);
        Assert.That(refusedAccept.ReadByte(), Is.EqualTo((byte) BuddyError.s_buddy_err_miss_id));
        Assert.That(refusedAccept.ReadLong(), Is.EqualTo(7));
        refusedAccept.ReadLong();
        refusedAccept.ReadLong();
        Assert.That(refusedAccept.ReadUnicodeString(), Is.Empty);
        Assert.That(refusedAccept.Available, Is.Zero);

        ByteReader refusedRemove = Open(BuddyPacket.Remove(7, BuddyError.s_buddy_err_miss_id), 7);
        refusedRemove.ReadByte();
        Assert.That(refusedRemove.ReadLong(), Is.EqualTo(7));
        refusedRemove.ReadLong();
        refusedRemove.ReadLong();
        Assert.That(refusedRemove.ReadUnicodeString(), Is.Empty);
        Assert.That(refusedRemove.Available, Is.Zero);
    }

    [Test]
    public void DeclineUnblockAndCancel_AreResultAndEntryId() {
        Buddy buddy = Make(7);
        foreach ((ByteWriter writer, byte mode) in new[] {
                     (BuddyPacket.Decline(buddy), (byte) 4),
                     (BuddyPacket.Unblock(buddy), (byte) 6),
                     (BuddyPacket.Cancel(buddy), (byte) 17),
                     (BuddyPacket.Decline(7, BuddyError.ok), (byte) 4),
                     (BuddyPacket.Unblock(7, BuddyError.ok), (byte) 6),
                     (BuddyPacket.Cancel(7, BuddyError.ok), (byte) 17),
                 }) {
            ByteReader reader = Open(writer, mode);
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadLong(), Is.EqualTo(7));
            Assert.That(reader.Available, Is.Zero);
        }
    }

    [Test]
    public void NotifyModes() {
        Buddy buddy = Make(7);
        ByteReader accept = Open(BuddyPacket.NotifyAccept(buddy), 11);
        Assert.That(accept.ReadLong(), Is.EqualTo(7));
        Assert.That(accept.Available, Is.Zero);

        ByteReader block = Open(BuddyPacket.NotifyBlock("Nm", BuddyError.s_buddy_err_miss_id), 12);
        Assert.That(block.ReadByte(), Is.EqualTo((byte) BuddyError.s_buddy_err_miss_id));
        Assert.That(block.ReadUnicodeString(), Is.EqualTo("Nm"));
        Assert.That(block.Available, Is.Zero);

        ByteReader online = Open(BuddyPacket.NotifyOnline(buddy), 14);
        Assert.That(online.ReadByte(), Is.EqualTo(buddy.Info.Online ? 0 : 1));
        Assert.That(online.ReadLong(), Is.EqualTo(7));
        Assert.That(online.ReadUnicodeString(), Is.EqualTo("bud"));
        Assert.That(online.Available, Is.Zero);

        ByteReader notices = Open(BuddyPacket.Notices([new BuddyNotice("A", BuddyNoticeType.Approved, 5), new BuddyNotice("B", BuddyNoticeType.Removed, 6)]), 13);
        Assert.That(notices.ReadInt(), Is.EqualTo(2));
        Assert.That(notices.ReadUnicodeString(), Is.EqualTo("A"));
        Assert.That(notices.ReadUnicodeString(), Is.EqualTo("approved"));
        Assert.That(notices.ReadLong(), Is.EqualTo(5));
        Assert.That(notices.ReadUnicodeString(), Is.EqualTo("B"));
        Assert.That(notices.ReadUnicodeString(), Is.EqualTo("removed"));
        Assert.That(notices.ReadLong(), Is.EqualTo(6));
        Assert.That(notices.Available, Is.Zero);
    }

    [Test]
    public void BodylessModesAndTheListEnd() {
        Assert.That(Open(BuddyPacket.StartList(), 15).Available, Is.Zero);
        Assert.That(Open(BuddyPacket.Forbidden(), 18).Available, Is.Zero);
        Assert.That(Open(BuddyPacket.AskAutoReject(), 20).Available, Is.Zero);
        ByteReader end = Open(BuddyPacket.EndList(3), 19);
        Assert.That(end.ReadInt(), Is.EqualTo(3));
        Assert.That(end.Available, Is.Zero);
    }
}

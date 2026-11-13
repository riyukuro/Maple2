using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Buddy Load (1), UpdateInfo (8) and Append (9): CBuddyManager::OnBuddy (buddymanager.cpp:95) reads the protocol byte
// (its response decoder, Unk_SPDB::ReadFrom serverpacketdef.cpp:115, reads nothing more for 1, 8 and 9), for Load an
// int count, then each entry with CBuddy::ReadFromPacket (buddy.cpp:188): long id, long, long, string name, string,
// short, 3 ints, short, 5 bytes, 2 ints, 3 strings, 3 ints, string, long, 3 ints.
public class BuddyEntryLayoutTests {
    private static (long Id, string Name, short Level, string Motto) ReadEntry(ByteReader reader) {
        long id = reader.ReadLong();
        reader.ReadLong();
        reader.ReadLong();
        string name = reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        reader.ReadShort();
        for (int i = 0; i < 3; i++) reader.ReadInt();
        short level = reader.ReadShort();
        for (int i = 0; i < 5; i++) reader.ReadByte();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadUnicodeString();
        string motto = reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        for (int i = 0; i < 3; i++) reader.ReadInt();
        reader.ReadUnicodeString();
        reader.ReadLong();
        for (int i = 0; i < 3; i++) reader.ReadInt();
        return (id, name, level, motto);
    }

    private static Buddy Make(long id, BuddyType type) {
        var info = new PlayerInfo(new CharacterInfo(3, 4, "b", "m", "", Gender.Male, Job.Newbie, 42), "home", new AchievementInfo(), []);
        return new Buddy(new BuddyEntry { Id = id, OwnerId = 1, BuddyId = 4, LastModified = 99, Type = type }, info);
    }

    private static ByteReader Open(ByteWriter writer, byte command) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Buddy));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        return reader;
    }

    [Test]
    public void Load_ReadsEveryEntry() {
        ByteReader reader = Open(BuddyPacket.Load([Make(7, BuddyType.Default)], [Make(8, BuddyType.Blocked)]), 1);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(ReadEntry(reader), Is.EqualTo((7L, "b", (short) 42, "m")));
        Assert.That(ReadEntry(reader), Is.EqualTo((8L, "b", (short) 42, "")));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(8)]
    [TestCase(9)]
    public void UpdateAndAppend_ReadOneEntry(int command) {
        Buddy buddy = Make(7, BuddyType.OutRequest);
        ByteReader reader = Open(command == 8 ? BuddyPacket.UpdateInfo(buddy) : BuddyPacket.Append(buddy), (byte) command);
        Assert.That(ReadEntry(reader).Id, Is.EqualTo(7));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

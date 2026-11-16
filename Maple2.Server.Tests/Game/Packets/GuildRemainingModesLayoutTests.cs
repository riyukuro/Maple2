using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The remaining Guild modes against CGuildManager::FUN_1413b6370 (client/ms2/guildmanager.cpp:303) and its readers:
// 10 (FUN_1413be7e0): unicode name, u8 rank; 12 (OnPacketMemberRenamed): unicode old name, unicode new name; 46
// (OnPacketJoinRequestWithdrawn): long request id; 63 (FUN_1413bee70): unicode leader name; 66 (OnPacketExpandCapacity):
// int capacity; 78 (FUN_1413c7810): unicode name; 117 (OnPacketVsGameMatchCanceled): none.
public class GuildRemainingModesLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Guild));
        return reader;
    }

    [Test]
    public void Modes() {
        ByteReader reader = Read(GuildPacket.UpdateMemberRank("Ann", 3));
        Assert.That(reader.ReadByte(), Is.EqualTo(10));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.UpdateMemberName("Ann", "Bea"));
        Assert.That(reader.ReadByte(), Is.EqualTo(12));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Bea"));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.WithdrawApplication(77));
        Assert.That(reader.ReadByte(), Is.EqualTo(46));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.UpdateLeader("Ann"));
        Assert.That(reader.ReadByte(), Is.EqualTo(63));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.UpdateCapacity(60));
        Assert.That(reader.ReadByte(), Is.EqualTo(66));
        Assert.That(reader.ReadInt(), Is.EqualTo(60));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.RemoveTag("Ann"));
        Assert.That(reader.ReadByte(), Is.EqualTo(78));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.VsGameMatchCanceled());
        Assert.That(reader.ReadByte(), Is.EqualTo(117));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

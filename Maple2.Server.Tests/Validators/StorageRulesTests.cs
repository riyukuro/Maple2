using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class StorageRulesTests {
    // RequestItemStorage 0x0018 mode 1: i64 storage id, i64 item uid, i16 slot, i32 count.
    [TestCase(1, 10, true)]
    [TestCase(10, 10, true)]
    [TestCase(11, 10, false)]
    [TestCase(0, 10, false)] // was read as the whole stack
    [TestCase(-1, 10, false)] // was read as the whole stack
    public void CountOfMovePacket(int count, int owned, bool expected) {
        var writer = new ByteWriter();
        writer.WriteShort(0x0018);
        writer.WriteByte(1);
        writer.WriteLong(0);
        writer.WriteLong(77);
        writer.WriteShort(3);
        writer.WriteInt(count);
        var packet = new ByteReader(writer.ToArray(), 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        packet.ReadLong();
        Assert.That(packet.ReadLong(), Is.EqualTo(77));
        Assert.That(packet.ReadShort(), Is.EqualTo(3));
        int read = packet.ReadInt();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(StorageRules.IsCount(read, owned), Is.EqualTo(expected));
    }

    [TestCase(5, 0, true)] // no pet out
    [TestCase(5, 6, true)]
    [TestCase(5, 5, false)] // the pet that is out stays in the inventory
    public void SummonedPetIsNotStored(long itemUid, long petUid, bool expected) {
        Assert.That(StorageRules.CanDeposit(itemUid, petUid), Is.EqualTo(expected));
    }

    [TestCase(1, 5, true)]
    [TestCase(0, 0, true)] // all of it goes onto stacks
    [TestCase(0, 2, false)] // a full inventory: the item stays in the storage
    public void WithdrawNeedsRoom(int openSlots, int notStackable, bool expected) {
        Assert.That(StorageRules.HasRoom(openSlots, notStackable), Is.EqualTo(expected));
    }
}

using System.Linq;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class DismantleRulesTests {
    // ItemDismantle 0x005A mode 1: i32 slot (-1), i64 item uid, i32 count.
    [TestCase(1, 5, false, 0L, true)]
    [TestCase(5, 5, false, 9L, true)]
    [TestCase(6, 5, false, 0L, false)]
    [TestCase(0, 5, false, 0L, false)]
    [TestCase(-1, 5, false, 0L, false)] // was consumed as the whole stack for no reward
    [TestCase(1, 5, true, 0L, false)] // locked
    [TestCase(1, 1, false, 42L, false)] // the pet that is out
    public void StagePacket(int count, int owned, bool locked, long petUid, bool expected) {
        var writer = new ByteWriter();
        writer.WriteShort(0x005A);
        writer.WriteByte(1);
        writer.WriteInt(-1);
        writer.WriteLong(42);
        writer.WriteInt(count);
        var packet = new ByteReader(writer.ToArray(), 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        Assert.That(packet.ReadInt(), Is.EqualTo(-1));
        long uid = packet.ReadLong();
        int read = packet.ReadInt();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(DismantleRules.CanStage(read, owned, locked, uid, petUid), Is.EqualTo(expected));
    }

    [TestCase(2500, 1000, new[] { 1000, 1000, 500 })]
    [TestCase(1000, 1000, new[] { 1000 })]
    [TestCase(3, 0, new[] { 1, 1, 1 })]
    [TestCase(0, 100, new int[0])]
    public void RewardStacksSumToTheReward(int total, int slotMax, int[] expected) {
        int[] stacks = DismantleRules.Stacks(total, slotMax).ToArray();
        Assert.That(stacks, Is.EqualTo(expected));
        Assert.That(stacks.Sum(), Is.EqualTo(total));
    }
}

using System.Collections.Generic;
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

    // The confirm's room check counts every reward at its max, one entry per stack.
    [Test]
    public void MaxRewardStacks_TakesTheMaxPerStack() {
        var rewards = new Dictionary<int, (int Min, int Max)> {
            [40100023] = (1400, 2000),
            [20000001] = (3, 3),
        };
        List<(int Id, int Amount)> stacks = DismantleRules.MaxRewardStacks(rewards, id => id == 40100023 ? 999 : 1);
        Assert.That(stacks, Is.EqualTo(new[] {
            (20000001, 1), (20000001, 1), (20000001, 1),
            (40100023, 999), (40100023, 999), (40100023, 2),
        }));
    }

    [Test]
    public void MaxRewardStacks_UnknownIdTakesNoRoom() {
        var rewards = new Dictionary<int, (int Min, int Max)> { [1] = (5, 5), [2] = (0, 0) };
        Assert.That(DismantleRules.MaxRewardStacks(rewards, id => id == 1 ? null : 10), Is.Empty);
    }

    // The outfit socket refund: 75% of the 690 unlock crystals per opened socket, at least 1, none at 0%.
    [TestCase(690, 75, 1, 517)]
    [TestCase(690, 75, 3, 1551)]
    [TestCase(690, 0, 3, 0)] // NA, JP
    [TestCase(690, 75, 0, 0)] // no socket opened
    [TestCase(1, 75, 2, 2)] // rounds down to 0, given as 1
    public void SkinSocketRefund(int unlockCount, int percent, int openSockets, int expected) {
        Assert.That(DismantleRules.SkinSocketRefund(unlockCount, percent, openSockets), Is.EqualTo(expected));
    }
}

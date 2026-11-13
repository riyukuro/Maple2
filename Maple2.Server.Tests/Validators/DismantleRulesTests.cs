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

    // Gear without a break row, as KMS2 pays it: onyx by the item's rarity 3 sell price.
    [TestCase(7139L, 3, false, 142)]
    [TestCase(7139L, 3, true, 71)] // one-handed: half
    [TestCase(1200L, 1, false, 2)]
    [TestCase(1200L, 2, false, 4)]
    [TestCase(1200L, 4, false, 32)]
    [TestCase(1200L, 4, true, 16)]
    [TestCase(1L, 4, false, 1)] // never below 1
    [TestCase(0L, 3, false, 1)]
    [TestCase(1200L, 5, false, 32)] // above rarity 4: not known, paid as rarity 4
    public void Onyx(long price, int rarity, bool oneHanded, int expected) {
        Assert.That(DismantleRules.Onyx(price, rarity, oneHanded), Is.EqualTo(expected));
    }

    [TestCase(1, false, 0, 0)]
    [TestCase(3, false, 0, 0)]
    [TestCase(4, false, 3, 3)]
    [TestCase(4, true, 1, 2)]
    public void ChaosOnyx(int rarity, bool oneHanded, int min, int max) {
        Assert.That(DismantleRules.ChaosOnyx(rarity, oneHanded), Is.EqualTo((min, max)));
    }

    // The result is min to max, max included.
    [Test]
    public void RollReachesBothEnds() {
        var random = new System.Random(3);
        var seen = new HashSet<int>();
        for (int i = 0; i < 200; i++) {
            seen.Add(DismantleRules.Roll(1, 2, random));
        }
        Assert.That(seen, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(DismantleRules.Roll(0, 0, random), Is.EqualTo(0));
        Assert.That(DismantleRules.Roll(5, 5, random), Is.EqualTo(5));
    }

    // The donation box (CItemBreakManager::TryAddItem, dialog type 2): breakable, and of type 1 or with a skin type.
    [TestCase(true, 1, 0, true)]
    [TestCase(true, 1, 3, true)]
    [TestCase(true, 2, 1, true)] // a skin type passes another type
    [TestCase(true, 2, 0, false)]
    [TestCase(true, 4, 0, false)]
    [TestCase(false, 1, 0, false)] // CanBreakItem first
    [TestCase(false, 1, 3, false)]
    public void CanDonate(bool breakable, int itemType, int skinType, bool expected) {
        Assert.That(DismantleRules.CanDonate(breakable, itemType, skinType), Is.EqualTo(expected));
    }
}

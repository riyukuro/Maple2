using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class LimitBreakRulesTests {
    // LimitBreak 0x00BC: u8 mode (0 preview, 1 enchant), i64 item uid.
    [TestCase((byte) 0)]
    [TestCase((byte) 1)]
    public void ModeAndUid(byte mode) {
        var writer = new ByteWriter();
        writer.WriteByte(mode);
        writer.WriteLong(0x0102030405060708);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadByte(), Is.EqualTo(mode));
        Assert.That(packet.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(5, 0, 15, true)]
    [TestCase(5, 0, 14, false)]
    [TestCase(5, 0, 0, false)] // a new item: no limit break and no enchant record
    [TestCase(0, 0, 15, false)] // the item has no limit break
    [TestCase(5, 1, 0, true)] // the enchant goes back to 0 after a limit break
    [TestCase(5, -1, 15, false)]
    public void Item(int maxLevel, int level, int enchants, bool expected) {
        Assert.That(LimitBreakRules.IsItem(maxLevel, level, enchants), Is.EqualTo(expected));
    }

    [TestCase(5, 4, false)]
    [TestCase(5, 5, true)]
    [TestCase(5, 6, true)]
    public void MaxLevel(int maxLevel, int level, bool expected) {
        Assert.That(LimitBreakRules.IsMaxLevel(maxLevel, level), Is.EqualTo(expected));
    }

    [TestCase(7, 1, 7, 0, true)]
    [TestCase(7, 1, 8, 0, false)] // another item was previewed
    [TestCase(7, 2, 7, 2, false)] // the preview was already paid for
    [TestCase(7, 2, 7, 0, false)]
    public void Staged(long stagedUid, int stagedLevel, long itemUid, int level, bool expected) {
        Assert.That(LimitBreakRules.IsStaged(stagedUid, stagedLevel, itemUid, level), Is.EqualTo(expected));
    }

    [TestCase(100, 100, true)]
    [TestCase(99, 100, false)]
    [TestCase(100, 0, true)]
    [TestCase(100, -1, false)]
    public void Pay(long meso, long cost, bool expected) {
        Assert.That(LimitBreakRules.CanPay(meso, cost), Is.EqualTo(expected));
    }

    // The fee in crystal fragments (meso / 1000), paid in owned fragments first and 1000 meso for each one lacking.
    [TestCase(6228500, 0, 6228, 0, 6228000)]
    [TestCase(6228500, 228, 6228, 228, 6000000)]
    [TestCase(6228500, 9000, 6228, 6228, 0)]
    [TestCase(1171000, -5, 1171, 0, 1171000)]
    [TestCase(0, 10, 0, 0, 0)]
    public void Fee(long mesoCost, long owned, int needed, int fragments, long meso) {
        Assert.That(LimitBreakRules.Fragments(mesoCost), Is.EqualTo(needed));
        Assert.That(LimitBreakRules.Fee(mesoCost, owned), Is.EqualTo((fragments, meso)));
    }
}

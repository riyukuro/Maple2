using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class LapenshardRulesTests {
    // One material entry of Lapenshard 5: i64 uid, i32 amount.
    private static bool Read(byte[] entry, int owned, long upgradedUid) {
        var packet = new ByteReader(entry, 0);
        long uid = packet.ReadLong();
        int amount = packet.ReadInt();
        return LapenshardRules.IsMaterialAmount(amount, owned, uid == upgradedUid);
    }

    private static byte[] Entry(long uid, int amount) {
        var writer = new ByteWriter();
        writer.WriteLong(uid);
        writer.WriteInt(amount);
        return writer.ToArray();
    }

    [Test]
    public void AmountIsOneToTheStack() {
        // Uid 7, amount 2.
        byte[] entry = { 0x07, 0, 0, 0, 0, 0, 0, 0, 0x02, 0, 0, 0 };
        Assert.That(Read(entry, owned: 2, upgradedUid: 1), Is.True);
        Assert.That(Read(entry, owned: 1, upgradedUid: 1), Is.False);
        Assert.That(Read(Entry(7, 0), owned: 5, upgradedUid: 1), Is.False);
        // Amount -2: FE FF FF FF. With a second entry of +N it made the sum fit the needed count.
        byte[] negative = { 0x07, 0, 0, 0, 0, 0, 0, 0, 0xFE, 0xFF, 0xFF, 0xFF };
        Assert.That(Read(negative, owned: 5, upgradedUid: 1), Is.False);
    }

    [Test]
    public void TheUpgradedShardIsNotItsOwnMaterial() {
        Assert.That(Read(Entry(7, 1), owned: 1, upgradedUid: 7), Is.False);
        Assert.That(Read(Entry(7, 3), owned: 3, upgradedUid: 7), Is.False);
        // A stack of three: one is upgraded, two may be materials.
        Assert.That(Read(Entry(7, 2), owned: 3, upgradedUid: 7), Is.True);
    }
}

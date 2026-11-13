using System.Collections.Generic;
using Maple2.Model.Metadata;
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

    // The effects of the worn shards: every additional effect of each shard that the wearer lacks, once.
    [Test]
    public void MissingEffects_OfTheWornShards() {
        var items = new Dictionary<int, ItemMetadataAdditionalEffect[]> {
            [901] = [new ItemMetadataAdditionalEffect(5001, 1, false), new ItemMetadataAdditionalEffect(5002, 3, false)],
            [902] = [new ItemMetadataAdditionalEffect(5002, 3, false), new ItemMetadataAdditionalEffect(5003, 1, true)],
        };
        IEnumerable<ItemMetadataAdditionalEffect>? EffectsOf(int itemId) => items.GetValueOrDefault(itemId);

        // Nothing held: both effects of the first shard; the second adds none (one shared, one pick-up only).
        Assert.That(LapenshardRules.MissingEffects([901, 902], EffectsOf, (_, _) => false),
            Is.EqualTo(new List<(int, short)> { (5001, 1), (5002, 3) }));
        // Held effects are not given again (a field change keeps them; a login has lost them).
        Assert.That(LapenshardRules.MissingEffects([901, 902], EffectsOf, (id, _) => id == 5001),
            Is.EqualTo(new List<(int, short)> { (5002, 3) }));
        // No shard worn, or a shard the item data does not hold: nothing.
        Assert.That(LapenshardRules.MissingEffects([], EffectsOf, (_, _) => false), Is.Empty);
        Assert.That(LapenshardRules.MissingEffects([999], EffectsOf, (_, _) => false), Is.Empty);
    }

    // The tier a fusion makes is the next item's LapenLevel (41000015 tier 5 -> 41000016 tier 6); a next item
    // the table does not list is one tier up.
    [Test]
    public void ResultTier_IsTheNextItemsLevel() {
        var table = new LapenshardUpgradeTable(new Dictionary<int, LapenshardUpgradeTable.Entry> {
            [41000015] = new(5, 41000011, 41000016, 8, [], 1_500_000),
            [41000016] = new(6, 41000011, 41000017, 14, [], 2_000_000),
        });
        Assert.That(LapenshardRules.ResultTier(table, table.Entries[41000015]), Is.EqualTo(6));
        Assert.That(LapenshardRules.ResultTier(table, table.Entries[41000016]), Is.EqualTo(7));
    }
}

using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The enchant transform window's checks (CItemEnchantTransformTable, itemenchanttransformtable.cpp).
public class EnchantTransformTests {
    private static ItemEnchantTransformTable.Entry Row(int limitLevel, int enchantLevel, long meso, int output) {
        return new ItemEnchantTransformTable.Entry(
            Rarity: 5,
            LimitLevel: limitLevel,
            EnchantLevel: enchantLevel,
            Slots: [14, 15],
            ItemIds: [100, 101],
            NeedMeso: meso,
            Outputs: [new ItemEnchantTransformTable.Output(900 + output, 6, 1)]);
    }

    private static readonly ItemEnchantTransformTable Table = new([
        Row(50, 9, 1000, 1),
        Row(50, 10, 2000, 2),
        Row(60, 10, 3000, 3),
    ]);

    [Test]
    public void Find_MatchesIdSlotEnchantAndRarity() {
        Assert.That(EnchantTransform.Find(Table, 100, 14, 9, 5)?.NeedMeso, Is.EqualTo(1000));
        Assert.That(EnchantTransform.Find(Table, 101, 15, 9, 5)?.NeedMeso, Is.EqualTo(1000));
    }

    [Test]
    public void Find_RefusesAnotherIdSlotEnchantOrRarity() {
        Assert.That(EnchantTransform.Find(Table, 102, 14, 9, 5), Is.Null);
        Assert.That(EnchantTransform.Find(Table, 100, 13, 9, 5), Is.Null);
        Assert.That(EnchantTransform.Find(Table, 100, 14, 8, 5), Is.Null);
        Assert.That(EnchantTransform.Find(Table, 100, 14, 9, 4), Is.Null);
    }

    // MakeKey's last part is the literal "(4)": the limit level is not in the key, and of two rows with the same
    // key the later one wins.
    [Test]
    public void Find_IgnoresLimitLevelAndTakesTheLastRow() {
        Assert.That(EnchantTransform.Find(Table, 100, 14, 10, 5)?.NeedMeso, Is.EqualTo(3000));
    }

    [Test]
    public void Locked_RefusesALockedOrUnlockingItem() {
        Assert.That(EnchantTransform.Locked(true, 0, 100), Is.True);
        Assert.That(EnchantTransform.Locked(false, 101, 100), Is.True);
        Assert.That(EnchantTransform.Locked(false, 100, 100), Is.False);
        Assert.That(EnchantTransform.Locked(false, 0, 100), Is.False);
    }
}

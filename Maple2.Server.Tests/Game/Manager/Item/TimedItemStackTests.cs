using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;

namespace Maple2.Server.Tests.Game.Manager.Item;

// The client never stacks an item with an expiry whose data gives it a lifetime (usePeriod): CItem::IsStackable,
// commonfiles/item.cpp:837. Items of weeks or months (usePeriod 0) and items without an expiry still stack.
public class TimedItemStackTests {
    private const long Expiry = 1_899_999_960; // the start of a minute
    private static long nextUid = 1;

    private static Model.Game.Item Make(ItemMetadataLife life, long expiry, int amount = 1) {
        var property = new ItemMetadataProperty(false, 0, 9999, 2, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var customize = new ItemMetadataCustomize(0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        // 20000069 in KR: life usePeriod 10080 (minutes), slotMax 9999.
        var metadata = new ItemMetadata(20000069, "20000069", [], "", [], life, property, customize, limit, null, null, [], null, null, null, null);
        return new Model.Game.Item(metadata, 1, amount) {
            Uid = nextUid++,
            ExpiryTime = expiry,
        };
    }

    private static readonly ItemMetadataLife UsePeriod = new(ExpirationDuration: 604800, ExpirationTimestamp: 0);
    private static readonly ItemMetadataLife Monthly = new(ExpirationDuration: 5097600, ExpirationTimestamp: 0, ExpirationType: 2, WeeksMonths: 2);

    [Test]
    public void ItemsOfAUsePeriod_WithTheSameExpiry_DoNotStack() {
        var collection = new ItemCollection(2);
        Model.Game.Item first = Make(UsePeriod, Expiry);
        Model.Game.Item second = Make(UsePeriod, Expiry);
        collection.Add(first);

        Assert.That(collection.GetStackResult(second), Is.EqualTo(1));
        IList<(Model.Game.Item Item, int Added)> results = collection.Add(second, stack: true);
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Item, Is.SameAs(second));
        Assert.That(first.Amount, Is.EqualTo(1));
        Assert.That(collection.Count, Is.EqualTo(2));
    }

    [Test]
    public void ItemsOfAUsePeriod_InTheSameMinute_KeepTheirOwnExpiry() {
        var collection = new ItemCollection(2);
        Model.Game.Item first = Make(UsePeriod, Expiry);
        Model.Game.Item second = Make(UsePeriod, Expiry + 30);
        collection.Add(first);
        collection.Add(second);

        Assert.That(collection.MergeStacks(), Is.Empty);
        Assert.That(collection.Get(second.Uid)?.ExpiryTime, Is.EqualTo(Expiry + 30));
    }

    [Test]
    public void ItemsOfMonths_WithTheSameExpiry_Stack() {
        var collection = new ItemCollection(1);
        Model.Game.Item first = Make(Monthly, Expiry);
        collection.Add(first);

        collection.Add(Make(Monthly, Expiry, amount: 3), stack: true);
        Assert.That(first.Amount, Is.EqualTo(4));
    }

    [Test]
    public void ItemsOfAUsePeriod_WithoutAnExpiry_Stack() {
        // A row stored with no expiry (the client's IsStackable asks the item's own expiry first).
        var collection = new ItemCollection(1);
        Model.Game.Item first = Make(UsePeriod, 0);
        collection.Add(first);

        collection.Add(Make(UsePeriod, 0, amount: 2), stack: true);
        Assert.That(first.Amount, Is.EqualTo(3));
    }
}

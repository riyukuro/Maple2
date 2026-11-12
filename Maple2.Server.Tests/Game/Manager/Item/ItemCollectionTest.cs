using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;

namespace Maple2.Server.Tests.Game.Manager.Item;

public class ItemCollectionTest {
    private static readonly Random Rng = new(123);

    [Test]
    public void AddSlotWithIndexer() {
        var item = CreateItem(1000);
        var otherItem = CreateItem(1000);
        var collection = new ItemCollection(1) {
            [0] = item,
        };
        Assert.That(collection[0], Is.EqualTo(item));
        Assert.That(item.Slot, Is.EqualTo(0));
        Assert.That(collection.Get(item.Uid), Is.EqualTo(item));

        collection[0] = null; // Not allowed, NOP
        Assert.That(item, Is.EqualTo(collection[0]));
        collection[0] = otherItem; // Slot taken, NOP
        Assert.That(item, Is.EqualTo(collection[0]));

        // Invalid slots, NOP
        collection[-1] = otherItem;
        collection[short.MaxValue] = otherItem;
    }

    [Test]
    public void TestAdd() {
        var item = CreateItem(1000, amount: 100);
        var collection = new ItemCollection(6);
        IList<(Model.Game.Item Item, int Added)> results = collection.Add(item);
        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Item, Is.EqualTo(item));
        Assert.That(results[0].Added, Is.EqualTo(100));
    }

    [Test]
    public void TestAddNoSlotsFree() {
        var item = CreateItem(1000);
        var otherItem = CreateItem(1000);
        var collection = new ItemCollection(1);
        collection.Add(item);

        Assert.That(collection.OpenSlots, Is.EqualTo(0));
        var results = collection.Add(otherItem);
        Assert.That(results.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestAddWithoutStacking() {
        var item = CreateItem(1000);
        var stackItem = CreateItem(1000);
        var collection = new ItemCollection(1);
        collection.Add(item);

        // Item can't stack because called with Add(stack=false)
        Assert.That(collection.OpenSlots, Is.EqualTo(0));
        var results = collection.Add(stackItem);
        Assert.That(results.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestAddWithStacking() {
        var item = CreateItem(1000, amount: 10);
        var stackItem = CreateItem(1000, amount: 50);
        var collection = new ItemCollection(1);
        collection.Add(item);

        // |stackItem| should be fully stackable.
        Assert.That(collection.GetStackResult(stackItem), Is.EqualTo(0));

        Assert.That(collection.OpenSlots, Is.EqualTo(0));
        IList<(Model.Game.Item Item, int Added)> results = collection.Add(stackItem, stack: true);
        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Item, Is.EqualTo(item)); // |stackItem| was stacked onto |item|.
        Assert.That(results[0].Added, Is.EqualTo(50));

        // |stackItem| should be mutated to have amount=0.
        Assert.That(stackItem.Amount, Is.EqualTo(0));
    }

    [Test]
    public void TestAddWithStackingMultipleSlots() {
        var item1 = CreateItem(1000, amount: 90);
        var item2 = CreateItem(1000, amount: 95);
        var item3 = CreateItem(1000, amount: 100);
        var stackItem = CreateItem(1000, amount: 50);
        var collection = new ItemCollection(4);
        collection.Add(item1);
        collection.Add(item2);
        collection.Add(item3);

        // |stackItem| should not be fully stackable.
        Assert.That(collection.GetStackResult(stackItem), Is.EqualTo(35));

        Assert.That(collection.OpenSlots, Is.EqualTo(1));
        IList<(Model.Game.Item Item, int Added)> results = collection.Add(stackItem, stack: true);
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results[0].Item, Is.EqualTo(item1));
        Assert.That(results[0].Added, Is.EqualTo(10));
        Assert.That(results[1].Item, Is.EqualTo(item2));
        Assert.That(results[1].Added, Is.EqualTo(5));
        Assert.That(results[2].Item, Is.EqualTo(stackItem));
        Assert.That(results[2].Added, Is.EqualTo(35));
    }

    [Test]
    public void TestAddWithStackingDoesNotFit() {
        var item1 = CreateItem(1000, amount: 90);
        var item2 = CreateItem(1000, amount: 95);
        var item3 = CreateItem(1000, amount: 100);
        var stackItem = CreateItem(1000, amount: 50);
        var collection = new ItemCollection(3);
        collection.Add(item1);
        collection.Add(item2);
        collection.Add(item3);

        Assert.That(collection.OpenSlots, Is.EqualTo(0));
        var results = collection.Add(stackItem, stack: true);
        Assert.That(results.Count, Is.EqualTo(0));

        // |collection| and |stackItem| are unchanged.
        Assert.That(collection.Get(item1.Uid)?.Amount, Is.EqualTo(90));
        Assert.That(collection.Get(item2.Uid)?.Amount, Is.EqualTo(95));
        Assert.That(collection.Get(item3.Uid)?.Amount, Is.EqualTo(100));
        Assert.That(stackItem.Amount, Is.EqualTo(50));
    }

    [Test]
    public void TestRetrieval() {
        var item1 = CreateItem(1000);
        var item2 = CreateItem(2000);
        var collection = new ItemCollection(6) {
            [2] = item1,
            [4] = item2,
        };

        Assert.That(collection.Contains(item1.Uid), Is.True);
        Assert.That(collection.Get(item1.Uid), Is.EqualTo(item1));
        Assert.That(collection.Contains(item2.Uid), Is.True);
        Assert.That(collection.Get(item2.Uid), Is.EqualTo(item2));
        Assert.That(collection.Contains(0), Is.False);
        Assert.IsNull(collection.Get(0));
    }

    [Test]
    public void TestRemove() {
        var item = CreateItem(1000);
        var collection = new ItemCollection(6);
        collection.Add(item);
        Assert.That(item, Is.EqualTo(collection[0]));

        // Remove valid item
        Assert.That(collection.Remove(item.Uid, out Model.Game.Item? removed), Is.True);
        Assert.That(removed, Is.EqualTo(item));
        Assert.IsNull(collection[0]);

        // Remove invalid item
        Assert.That(collection.Remove(-1, out _), Is.False);
    }

    [Test]
    public void TestRemoveSlot() {
        var item = CreateItem(1000);
        var collection = new ItemCollection(2);
        collection.Add(item);
        Assert.That(item, Is.EqualTo(collection[0]));

        // Remove invalid item
        Assert.That(collection.RemoveSlot(1, out _), Is.False);

        // Remove valid item
        Assert.That(collection.RemoveSlot(0, out Model.Game.Item? removed), Is.True);
        Assert.That(removed, Is.EqualTo(item));
        Assert.IsNull(collection[2]);

        // Remove invalid item
        Assert.That(collection.RemoveSlot(0, out _), Is.False);
    }

    [Test]
    public void TestSort() {
        var item1 = CreateItem(3000, rarity: 1, amount: 5);
        var item2 = CreateItem(1000, rarity: 3, amount: 10);
        var item3 = CreateItem(1000, rarity: 2, amount: 5);
        var item4 = CreateItem(1000, rarity: 3, amount: 1);
        var item5 = CreateItem(2000, rarity: 4, amount: 20);
        item5.Slot = 6; // Create a gap in inventory

        var collection = new ItemCollection(6);
        collection.Add(item1);
        collection.Add(item2);
        collection.Add(item3);
        collection.Add(item4);
        collection.Add(item5);

        collection.Sort();
        Assert.That(item3, Is.EqualTo(collection[0]));
        Assert.That(item4, Is.EqualTo(collection[1]));
        Assert.That(item2, Is.EqualTo(collection[2]));
        Assert.That(item5, Is.EqualTo(collection[3]));
        Assert.That(item1, Is.EqualTo(collection[4]));
        Assert.IsNull(collection[5]);
    }

    [Test]
    public void TestExpand() {
        var collection = new ItemCollection(1);
        Assert.That(collection.OpenSlots, Is.EqualTo(1));
        Assert.That(collection.Size, Is.EqualTo(1));

        Assert.That(collection.Expand(2), Is.True);
        Assert.That(collection.OpenSlots, Is.EqualTo(2));
        Assert.That(collection.Size, Is.EqualTo(2));
    }

    [Test]
    public void TestExpandNegative() {
        var collection = new ItemCollection(1);
        Assert.That(collection.Expand(-1), Is.False);
    }

    [Test]
    public void TestGetStackResult() {
        var item1 = CreateItem(1000, amount: 90);
        var item2 = CreateItem(1000, amount: 95);
        var item3 = CreateItem(1000, amount: 100);
        var collection = new ItemCollection(6);
        collection.Add(item1);
        collection.Add(item2);
        collection.Add(item3);

        var stackItem = CreateItem(1000, amount: 50);
        Assert.That(collection.GetStackResult(stackItem), Is.EqualTo(35));
        Assert.That(collection.GetStackResult(stackItem, amount: int.MinValue), Is.EqualTo(35));
        Assert.That(collection.GetStackResult(stackItem, amount: int.MaxValue), Is.EqualTo(35));
        Assert.That(collection.GetStackResult(stackItem, amount: 0), Is.EqualTo(0));
        Assert.That(collection.GetStackResult(stackItem, amount: 10), Is.EqualTo(0));
        Assert.That(collection.GetStackResult(stackItem, amount: 20), Is.EqualTo(5));
    }

    [Test]
    public void TestEnumeration() {
        var item1 = CreateItem(3000);
        var item2 = CreateItem(1000);
        var item3 = CreateItem(2000);
        var collection = new ItemCollection(10) {
            [1] = item1,
            [3] = item2,
            [5] = item3,
        };

        CollectionAssert.AreEqual(new[] {
            item1,
            item2,
            item3,
        }, collection.ToList());
    }

    [Test]
    public void TestSortFullInventory() {
        var item1 = CreateItem(3000, rarity: 1, amount: 5);
        var item2 = CreateItem(1000, rarity: 3, amount: 10);
        var item3 = CreateItem(1000, rarity: 2, amount: 5);
        var item4 = CreateItem(1000, rarity: 3, amount: 1);
        var item5 = CreateItem(2000, rarity: 4, amount: 20);

        // Create a full collection
        var collection = new ItemCollection(5);
        collection.Add(item1); // slot 0
        collection.Add(item2); // slot 1
        collection.Add(item3); // slot 2
        collection.Add(item4); // slot 3
        collection.Add(item5); // slot 4

        // Verify it's full
        Assert.That(collection.OpenSlots, Is.EqualTo(0));

        // Sort the full inventory
        collection.Sort();

        // Verify items are sorted correctly
        Assert.That(item3, Is.EqualTo(collection[0]));
        Assert.That(item4, Is.EqualTo(collection[1]));
        Assert.That(item2, Is.EqualTo(collection[2]));
        Assert.That(item5, Is.EqualTo(collection[3]));
        Assert.That(item1, Is.EqualTo(collection[4]));

        // Verify all items still exist
        Assert.That(collection.Count, Is.EqualTo(5));
    }

    [Test]
    public void TestSortWithNullItems() {
        var item1 = CreateItem(3000, rarity: 1, amount: 5);
        var item2 = CreateItem(1000, rarity: 3, amount: 10);
        var item3 = CreateItem(1000, rarity: 2, amount: 5);

        // Create collection with gaps
        var collection = new ItemCollection(10) {
            [2] = item1, // Insert at specific slots to create gaps
            [5] = item2,
            [8] = item3,
        };

        // Sort the inventory with gaps
        collection.Sort();

        // Verify items are sorted correctly with no gaps at the beginning
        Assert.That(item3, Is.EqualTo(collection[0]));
        Assert.That(item2, Is.EqualTo(collection[1]));
        Assert.That(item1, Is.EqualTo(collection[2]));
        Assert.IsNull(collection[3]);

        // Verify all items still exist
        Assert.That(collection.Count, Is.EqualTo(3));
    }

    [Test]
    public void TestSortAfterRemoving() {
        var item1 = CreateItem(3000, rarity: 1, amount: 5);
        var item2 = CreateItem(1000, rarity: 3, amount: 10);
        var item3 = CreateItem(1000, rarity: 2, amount: 5);
        var item4 = CreateItem(1000, rarity: 3, amount: 1);
        var item5 = CreateItem(2000, rarity: 4, amount: 20);

        // Create a full collection
        var collection = new ItemCollection(5);
        collection.Add(item1);
        collection.Add(item2);
        collection.Add(item3);
        collection.Add(item4);
        collection.Add(item5);

        // Remove an item, creating a gap
        collection.RemoveSlot(2, out _);

        // Sort the inventory with a gap
        collection.Sort();

        // Verify items are sorted correctly
        Assert.That(item4, Is.EqualTo(collection[0]));
        Assert.That(item2, Is.EqualTo(collection[1]));
        Assert.That(item5, Is.EqualTo(collection[2]));
        Assert.That(item1, Is.EqualTo(collection[3]));
        Assert.IsNull(collection[4]);

        // Verify item count
        Assert.That(collection.Count, Is.EqualTo(4));
    }

    // InventoryManager.Add creates a new item's row only when Fits is true, so Fits must say what Add does: on a
    // full bag a dungeon reward that does not fit is mailed, and a row made first was a second copy of it.
    [Test]
    public void Fits_IsWhatAddTakes() {
        var bag = new ItemCollection(2);
        bag.Add(CreateItem(1000, amount: 50));
        bag.Add(CreateItem(3000, amount: 100));

        Model.Game.Item gear = CreateItem(2000);
        Model.Game.Item fitsStack = CreateItem(1000, amount: 50);
        Model.Game.Item overStack = CreateItem(1000, amount: 51);
        Assert.Multiple(() => {
            Assert.That(bag.Fits(gear, stack: true), Is.False);
            Assert.That(bag.Add(gear, stack: true), Is.Empty);
            Assert.That(bag.Fits(overStack, stack: true), Is.False);
            Assert.That(bag.Add(overStack, stack: true), Is.Empty);
            Assert.That(bag.Fits(fitsStack, stack: false), Is.False);
            Assert.That(bag.Fits(fitsStack, stack: true), Is.True);
            Assert.That(bag.Add(fitsStack, stack: true), Is.Not.Empty);
            Assert.That(bag.Count, Is.EqualTo(2));
        });
    }

    // A failed Add changes nothing, so InventoryManager.Add can leave an item that already has a row where it was
    // (a mail attachment collected into a full bag stays in the mail) instead of discarding it.
    [Test]
    public void FailedAdd_LeavesTheItemAndTheBagAsTheyWere() {
        var bag = new ItemCollection(2);
        Model.Game.Item stack = CreateItem(1000, amount: 90);
        bag.Add(stack);
        bag.Add(CreateItem(3000, amount: 100));

        Model.Game.Item attachment = CreateItem(1000, amount: 20);
        attachment.Slot = 1;
        Assert.Multiple(() => {
            Assert.That(bag.Add(attachment, stack: true), Is.Empty);
            Assert.That(attachment.Amount, Is.EqualTo(20));
            Assert.That(attachment.Slot, Is.EqualTo(1));
            Assert.That(stack.Amount, Is.EqualTo(90));
            Assert.That(bag.Contains(attachment.Uid), Is.False);
            Assert.That(bag.Count, Is.EqualTo(2));
        });
    }

    // A trade's items arrive with their trade window slots (0-4) as their slots; SlotMax is 100.
    [Test]
    public void TakesAll_CountsTheSlotsOfEachItemInTurn() {
        var bag = new ItemCollection(3);
        bag.Add(CreateItem(1000, amount: 90));
        bag.Add(CreateItem(2000));
        Model.Game.Item Offered(int id, int amount, short slot) {
            Model.Game.Item item = CreateItem(id, amount: amount);
            item.Slot = slot;
            return item;
        }

        Assert.Multiple(() => {
            // Slot 0 is taken: 10 join the stack, the other item takes the one open slot.
            Assert.That(bag.TakesAll([Offered(1000, 10, 0), Offered(3000, 1, 1)]), Is.True);
            // 15 do not all join the stack: 5 need the open slot, the other item has none.
            Assert.That(bag.TakesAll([Offered(1000, 15, 0), Offered(3000, 1, 1)]), Is.False);
            // Slot 2 is free, so the item goes there without stacking and takes the last slot.
            Assert.That(bag.TakesAll([Offered(1000, 10, 2), Offered(3000, 1, 0)]), Is.False);
            // Two offered items of one id stack on each other in the slot the first one took.
            Assert.That(bag.TakesAll([Offered(4000, 40, 0), Offered(4000, 60, 1)]), Is.True);
            Assert.That(bag.TakesAll([Offered(4000, 40, 0), Offered(4000, 61, 1)]), Is.False);
            Assert.That(bag.TakesAll([]), Is.True);
            Assert.That(bag.Count, Is.EqualTo(2));
            Assert.That(bag.Single(item => item.Id == 1000).Amount, Is.EqualTo(90));
        });
    }

    private static Model.Game.Item CreateItem(int id, int rarity = 1, int amount = 1) {
        var fakeProperty = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var fakeCustomize = new ItemMetadataCustomize(0, 0);
        var fakeLimit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var fakeLife = new ItemMetadataLife(0, 0);
        var fakeMetadata = new ItemMetadata(id, $"{id}", [], "", [], fakeLife, fakeProperty, fakeCustomize, fakeLimit, null, null, [], null, null, null, null);
        return new Model.Game.Item(fakeMetadata, rarity, amount) {
            Uid = Rng.NextInt64(),
        };
    }
}

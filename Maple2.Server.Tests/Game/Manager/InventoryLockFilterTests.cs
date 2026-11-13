using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using BagItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// A price or recipe takes no locked item and none still unlocking (CItem::FUN_140528e20), as the client's counts
// leave them out (GetItemCountByFilter, inventory.cpp:743-766): Consume(IngredientInfo) by tag and
// ConsumeItemComponents by id and by tag. They took any item not expired.
public class InventoryLockFilterTests {
    private const int Id = 30000001;
    private const ItemTag Tag = ItemTag.ItemExtraction;
    private static readonly long Later = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

    private static (GameSession, BagItem Locked, BagItem Free) Start(bool locked) {
        var test = new TestGameSession();
        ItemMetadata data = TestGameSession.Metadata(Id, type: 1, slotMax: 1);
        data = data with { Property = data.Property with { Tag = Tag } };
        BagItem lockedItem = test.Store(data);
        BagItem freeItem = test.Store(data);
        GameSession session = test.Start();
        lockedItem = session.Item.Inventory.Get(lockedItem.Uid)!;
        freeItem = session.Item.Inventory.Get(freeItem.Uid)!;
        if (locked) {
            lockedItem.IsLocked = true;
        } else {
            lockedItem.UnlockTime = Later;
        }
        return (session, lockedItem, freeItem);
    }

    private static bool Held(GameSession session, BagItem item) => session.Item.Inventory.Get(item.Uid) != null;

    [TestCase(true)]
    [TestCase(false)]
    public void ConsumeByTag_SkipsLocked(bool locked) {
        (GameSession session, BagItem lockedItem, BagItem freeItem) = Start(locked);

        Assert.That(session.Item.Inventory.Consume([new IngredientInfo(Tag, 2)]), Is.False);
        Assert.That(session.Item.Inventory.Consume([new IngredientInfo(Tag, 1)]), Is.True);
        Assert.Multiple(() => {
            Assert.That(Held(session, lockedItem), Is.True);
            Assert.That(Held(session, freeItem), Is.False);
        });
    }

    [TestCase(true, ItemTag.None)]
    [TestCase(false, ItemTag.None)]
    [TestCase(true, Tag)]
    [TestCase(false, Tag)]
    public void ConsumeItemComponents_SkipsLocked(bool locked, ItemTag tag) {
        (GameSession session, BagItem lockedItem, BagItem freeItem) = Start(locked);

        Assert.That(session.Item.Inventory.ConsumeItemComponents([new ItemComponent(Id, -1, 2, tag)]), Is.False);
        Assert.That(session.Item.Inventory.ConsumeItemComponents([new ItemComponent(Id, -1, 1, tag)]), Is.True);
        Assert.Multiple(() => {
            Assert.That(Held(session, lockedItem), Is.True);
            Assert.That(Held(session, freeItem), Is.False);
        });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The key and the box of a key box are taken by one Consume (ItemBoxManager): both are tested before either is taken.
public class InventoryConsumeTests {
    private const int KeyId = 20300001;
    private const int BoxId = 20300002;
    private const ItemTag KeyTag = ItemTag.FreeChannelChatCoupon;
    private const ItemTag BoxTag = ItemTag.FreeWorldChatCoupon;

    private static ItemMetadata Tagged(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    [Test]
    public void KeyAndBox_AreTakenTogether() {
        var test = new TestGameSession();
        test.Store(Tagged(KeyId, KeyTag), 2);
        test.Store(Tagged(BoxId, BoxTag), 3);
        GameSession session = test.Start();

        bool taken = session.Item.Inventory.Consume([new IngredientInfo(KeyTag, 1), new IngredientInfo(BoxTag, 1)]);

        Assert.Multiple(() => {
            Assert.That(taken, Is.True);
            Assert.That(Held(session, KeyId), Is.EqualTo(1));
            Assert.That(Held(session, BoxId), Is.EqualTo(2));
        });
    }

    [Test]
    public void MissingBox_LeavesTheKey() {
        var test = new TestGameSession();
        test.Store(Tagged(KeyId, KeyTag), 2);
        GameSession session = test.Start();

        bool taken = session.Item.Inventory.Consume([new IngredientInfo(KeyTag, 1), new IngredientInfo(BoxTag, 1)]);

        Assert.Multiple(() => {
            Assert.That(taken, Is.False);
            Assert.That(Held(session, KeyId), Is.EqualTo(2));
        });
    }

    // A key and a box of one tag can be asked as one entry of the sum.
    [Test]
    public void KeyAndBoxOfOneTag_AreTakenAsTheSum() {
        var test = new TestGameSession();
        test.Store(Tagged(KeyId, KeyTag), 1);
        test.Store(Tagged(BoxId, KeyTag), 2);
        GameSession session = test.Start();

        Assert.That(session.Item.Inventory.Consume([new IngredientInfo(KeyTag, 4)]), Is.False);
        Assert.That(Held(session, KeyId) + Held(session, BoxId), Is.EqualTo(3));
        Assert.That(session.Item.Inventory.Consume([new IngredientInfo(KeyTag, 2)]), Is.True);
        Assert.That(Held(session, KeyId) + Held(session, BoxId), Is.EqualTo(1));
    }

    // Two entries of one tag add up: 2 + 2 of a tag held 3 times is refused with nothing taken (before, the index
    // threw on the second entry), and 1 + 2 takes all three.
    [Test]
    public void TwoEntriesOfOneTag_AreCountedOnce() {
        var test = new TestGameSession();
        test.Store(Tagged(KeyId, KeyTag), 1);
        test.Store(Tagged(BoxId, KeyTag), 2);
        GameSession session = test.Start();

        Assert.That(session.Item.Inventory.Consume([new IngredientInfo(KeyTag, 2), new IngredientInfo(KeyTag, 2)]), Is.False);
        Assert.That(Held(session, KeyId) + Held(session, BoxId), Is.EqualTo(3));
        Assert.That(session.Item.Inventory.Consume([new IngredientInfo(KeyTag, 1), new IngredientInfo(KeyTag, 2)]), Is.True);
        Assert.That(Held(session, KeyId) + Held(session, BoxId), Is.EqualTo(0));
    }

    // ConsumeNow (a roulette spin's ticket, a box, a map, a pouch) stores the take before the caller gives anything:
    // the row is lowered, or discarded (owner 0) for the last one, with no session save. Before, the take stayed in
    // memory until the next save while the prize was stored at once, so a crash in between let the claim run again.
    [Test]
    public void ConsumeNow_StoresTheTakeAtOnce() {
        var test = new TestGameSession();
        Maple2.Model.Game.Item ticket = test.Store(TestGameSession.Metadata(KeyId), 2);
        Maple2.Model.Game.Item last = test.Store(TestGameSession.Metadata(BoxId), 1);
        GameSession session = test.Start();

        bool taken = session.Item.Inventory.ConsumeNow(session.Item.Inventory.PlanItemComponents([
            new ItemComponent(KeyId, -1, 1, ItemTag.None), new ItemComponent(BoxId, -1, 1, ItemTag.None),
        ]));

        Assert.Multiple(() => {
            Assert.That(taken, Is.True);
            Assert.That(Held(session, KeyId), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Get(last.Uid), Is.Null);
            Assert.That(test.Row(ticket.Uid)?.Amount, Is.EqualTo(1), "the lowered stack is stored");
            Assert.That(test.Row(last.Uid)?.OwnerId, Is.EqualTo(0), "the last one is stored discarded");
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Two rows paid from one stack reserve their sum: a stack of 2 cannot pay two rows of 2 (each row alone fit, and
    // the second take then failed with the first one done).
    [Test]
    public void PlanItemComponents_TwoRowsOfOneStackNeedTheirSum() {
        var test = new TestGameSession();
        test.Store(TestGameSession.Metadata(KeyId), 2);
        GameSession session = test.Start();
        var row = new ItemComponent(KeyId, -1, 2, ItemTag.None);

        Assert.That(session.Item.Inventory.PlanItemComponents([row, row]), Is.Null);
        Assert.That(session.Item.Inventory.PlanItemComponents([row with { Amount = 1 }, row with { Amount = 1 }]),
            Is.EqualTo(new[] { (session.Item.Inventory.Find(KeyId).Single().Uid, 2) }));
        Assert.That(Held(session, KeyId), Is.EqualTo(2), "a plan takes nothing");
    }
}

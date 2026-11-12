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

    // A key and a box of one tag are asked as one entry of the sum (two entries of a tag cannot be indexed).
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
}

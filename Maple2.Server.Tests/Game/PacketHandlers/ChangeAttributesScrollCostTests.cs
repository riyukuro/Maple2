using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The scroll remake's costs (ChangeAttributesScrollHandler.TakeCosts): the scroll, then the lock item, or neither.
public class ChangeAttributesScrollCostTests {
    private const int ScrollId = 20000004;
    private const int LockId = 20000005;
    private TestGameSession test = null!;

    private Item Held(GameSession session, int itemId) {
        Item item = session.Field!.ItemDrop.CreateItem(itemId)!;
        Assert.That(session.Item.Inventory.Add(item, commit: true), Is.True);
        return session.Item.Inventory.Find(itemId).Single();
    }

    // An expired scroll is refused and the lock item stays in the bag, and later moves keep its row. Before, the lock
    // item was taken (its row went on the bag's delete list, which was never emptied then) and given back as a copy with its
    // uid: the bag's copy wrote the row back at each save while it stayed, but once the item left the bag (here to
    // the storage, as Deposit and the storage's close do) the next save of the bag deleted the stored item.
    [Test]
    public void ExpiredScroll_KeepsTheLockItem() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ScrollId), TestGameSession.Metadata(LockId));
        GameSession session = test.Start();
        Item scroll = Held(session, ScrollId);
        Item lockItem = Held(session, LockId);
        scroll.ExpiryTime = 1;

        ChangeAttributesScrollError error;
        lock (session.Item) {
            error = ChangeAttributesScrollHandler.TakeCosts(session, scroll, lockItem);
        }
        int held = session.Item.Inventory.Find(LockId).Sum(item => item.Amount);
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Save(db);
        }
        Item stored;
        lock (session.Item) {
            Assert.That(session.Item.Inventory.Remove(lockItem.Uid, out stored!), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(session.AccountId, stored), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Save(db);
        }

        using var context = new Ms2Context(test.Options);
        Assert.Multiple(() => {
            Assert.That(error, Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_consume_scroll));
            Assert.That(held, Is.EqualTo(1));
            Assert.That(context.Item.Single(row => row.Id == lockItem.Uid).OwnerId, Is.EqualTo(session.AccountId));
            Assert.That(context.Item.Count(row => row.ItemId == ScrollId && row.OwnerId == test.CharacterId), Is.EqualTo(1));
        });
    }
}

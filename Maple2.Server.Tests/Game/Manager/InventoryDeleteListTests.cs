using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Session;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// The bag's delete list (InventoryManager.delete): the rows a save writes to owner 0.
public class InventoryDeleteListTests {
    private const int ItemId = 20000005;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId));
        return test.Start();
    }

    private static StoredItem Held(GameSession session) {
        StoredItem item = session.Field!.ItemDrop.CreateItem(ItemId)!;
        Assert.That(session.Item.Inventory.Add(item, commit: true), Is.True);
        return session.Item.Inventory.Find(ItemId).Single();
    }

    private static List<StoredItem> Deletes(GameSession session) =>
        (List<StoredItem>) typeof(InventoryManager).GetField("delete", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Item.Inventory)!;

    private long Owner(long uid) {
        using var context = new Ms2Context(test.Options);
        return context.Item.Single(row => row.Id == uid).OwnerId;
    }

    // An item taken and given back as a copy with its uid, then moved out of the bag (to the account here), keeps
    // the new owner at the bag's next save. Before, its uid stayed on the delete list and the save wrote it to 0.
    [Test]
    public void TakenThenGivenBack_KeepsItsLaterOwner() {
        GameSession session = Start();
        StoredItem item = Held(session);
        StoredItem stored;
        lock (session.Item) {
            Assert.That(session.Item.Inventory.Consume(item.Uid, 1), Is.True);
            Assert.That(session.Item.Inventory.Add(item.Clone()), Is.True);
            Assert.That(session.Item.Inventory.Remove(item.Uid, out stored!), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(session.AccountId, stored), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Inventory.Save(db);
        }

        Assert.That(Owner(item.Uid), Is.EqualTo(session.AccountId));
    }

    // A committed save empties the list (before, it was never emptied); a save that is not committed keeps it, so
    // the next save writes the delete again. (The in-memory database ignores transactions, so only the list is
    // checked after the uncommitted save, not the row.)
    [Test]
    public void CommittedSave_EmptiesTheList_UncommittedKeepsIt() {
        GameSession session = Start();
        StoredItem item = Held(session);
        lock (session.Item) {
            Assert.That(session.Item.Inventory.Consume(item.Uid, 1), Is.True);
        }
        Assert.That(Deletes(session), Has.Count.EqualTo(1));

        using (GameStorage.Request db = test.Storage.Context()) {
            db.BeginTransaction();
            session.Item.Inventory.Save(db);
        }
        int afterRollback = Deletes(session).Count;

        using (GameStorage.Request db = test.Storage.Context()) {
            db.BeginTransaction();
            session.Item.Inventory.Save(db);
            Assert.That(db.Commit(), Is.True);
        }

        Assert.Multiple(() => {
            Assert.That(afterRollback, Is.EqualTo(1));
            Assert.That(Deletes(session), Is.Empty);
            Assert.That(Owner(item.Uid), Is.Zero);
        });
    }
}

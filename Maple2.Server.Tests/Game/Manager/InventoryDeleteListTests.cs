using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// The bag's delete list (InventoryManager.delete): the rows a save writes to owner 0.
public class InventoryDeleteListTests {
    private const int ItemId = 20000005;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId, slotMax: 10));
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

    // An item on the list given back to the bag (an add without commit) between the session save's bag save and its
    // commit: the committed owner-0 write is followed by a write back to the character. Before, the row stayed owner
    // 0 while the bag held the item, until the next save (a crash then lost it).
    [Test]
    public void GivenBackBeforeTheCommit_RowIsTheCharactersAgain() {
        GameSession session = Start();
        StoredItem item = Held(session);
        lock (session.Item) {
            Assert.That(session.Item.Inventory.Consume(item.Uid, 1), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            db.BeginTransaction();
            session.Item.Inventory.Save(db);
            lock (session.Item) {
                Assert.That(session.Item.Inventory.Add(item.Clone()), Is.True);
            }
            Assert.That(db.Commit(), Is.True);
        }

        Assert.Multiple(() => {
            Assert.That(Owner(item.Uid), Is.EqualTo(test.CharacterId));
            Assert.That(Deletes(session), Is.Empty);
        });
    }

    // An item on the list whose commit add fails keeps its entry, so the next save still writes the row to owner 0.
    // Before, the entry was taken off before the save that failed, and the row stayed the character's with the item
    // in neither the bag nor the list (the next load gave it back, while the caller kept or mailed it).
    [Test]
    public void FailedCommitAdd_KeepsTheEntry() {
        GameSession session = Start();
        StoredItem item = Held(session);
        lock (session.Item) {
            Assert.That(session.Item.Inventory.Consume(item.Uid, 1), Is.True);
        }
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                                                                               && entry.Entity.GetType().Name == "Item");
        bool added;
        try {
            added = session.Item.Inventory.Add(item.Clone(), commit: true);
        } finally {
            test.FailSave = null;
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Inventory.Save(db);
        }

        Assert.Multiple(() => {
            Assert.That(added, Is.False);
            Assert.That(session.Item.Inventory.Get(item.Uid), Is.Null);
            Assert.That(Owner(item.Uid), Is.Zero);
        });
    }

    // A new item whose commit add fails, and whose row's undo fails too, has its row written to owner 0 at the next
    // bag save. Before, the row stayed the character's at slot -1 while the caller mailed the item as a new row, so
    // the next load gave it twice.
    [Test]
    public void FailedAddAndFailedUndo_RowGoesAtTheNextSave() {
        GameSession session = Start();
        Held(session);
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                                                                               && entry.Entity.GetType().Name == "Item");
        bool added = session.Item.Inventory.Add(session.Field!.ItemDrop.CreateItem(ItemId)!, commit: true);
        test.FailSave = null;
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Inventory.Save(db);
        }

        using var context = new Ms2Context(test.Options);
        Assert.Multiple(() => {
            Assert.That(added, Is.False);
            Assert.That(session.Item.Inventory.Find(ItemId).Sum(item => item.Amount), Is.EqualTo(1));
            Assert.That(context.Item.Where(row => row.ItemId == ItemId && row.OwnerId == test.CharacterId).Sum(row => row.Amount), Is.EqualTo(1));
        });
    }
}

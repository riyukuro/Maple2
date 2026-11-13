using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Tools.Scheduler;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// A save writes only the item rows whose values changed since that item was last stored: a session save wrote every
// row of the bag, storage and equips each time (500-1300 ms for 1024 storage rows). A changed, an added and a removed
// item are stored; an unchanged one is not written again. A save rolled back is not counted as stored.
// MySQL: the rows are changed behind the server with SQL (the tests need MS2_TEST_GAME_DB).
public class ItemSaveTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private DbContextOptions options = null!;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the rows are changed with MySQL SQL: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        test = new TestGameSession(options);
        // As a session that took the account lock at its entry (GameSession.EnterServer).
        held = GameStorage.HoldOwners(test.AccountId, test.CharacterId);
    }

    [TearDown]
    public void TearDown() {
        held?.Dispose();
        held = null;
        GameStorage.AfterSeedingQuery = null;
    }

    private IDisposable? held;

    // The managers a full save writes, as SessionSaveTransactionTests sets them.
    private void StartSaving() {
        test.Start();
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Config = new ConfigManager(db, session);
        }
        session.Survival = new SurvivalManager(session);
        typeof(GameSession).GetField(nameof(GameSession.Scheduler))!.SetValue(session, new EventQueue(Serilog.Log.Logger));
    }

    // Sets a row's amount behind the server: a save that writes the row puts the server's amount back.
    private void SetStoredAmount(long uid, int amount) {
        using var context = new Ms2Context(options);
        Assume.That(context.Database.ExecuteSqlRaw("UPDATE `item` SET `Amount` = {0} WHERE `Id` = {1}", amount, uid), Is.EqualTo(1));
    }

    private (long Owner, int Amount)? Stored(long uid) {
        using var context = new Ms2Context(options);
        Maple2.Database.Model.Item? row = context.Item.AsNoTracking().SingleOrDefault(item => item.Id == uid);
        return row == null ? null : (row.OwnerId, row.Amount);
    }

    [Test]
    public void SessionSave_WritesChangedAddedRemoved_NotUnchanged() {
        ItemMetadata[] metadata = Enumerable.Range(0, 4).Select(i => TestGameSession.Metadata(50000301 + i)).ToArray();
        long changed = test.Store(metadata[0], 2).Uid;
        long unchanged = test.Store(metadata[1], 2).Uid;
        long removed = test.Store(metadata[2], 2).Uid;
        StartSaving();
        Assume.That(session.SessionSave(), Is.True, "first save");

        session.Item.Inventory.Get(changed)!.Amount = 5;
        Assume.That(session.Item.Inventory.Consume(removed), Is.True, "consume");
        // Added after the last save (a row made elsewhere, as a mail attachment's): written although its values are
        // the row's, since no save of this server stored it (its row changed behind the server shows the write).
        Item add = test.Store(metadata[3], 3);
        Assume.That(session.Item.Inventory.Add(add), Is.True, "add");
        long added = add.Uid;
        SetStoredAmount(added, 77);
        SetStoredAmount(unchanged, 77);
        Assert.That(session.SessionSave(), Is.True);

        Assert.Multiple(() => {
            Assert.That(Stored(changed), Is.EqualTo(((long, int)?) (test.CharacterId, 5)), "changed");
            Assert.That(Stored(added), Is.EqualTo(((long, int)?) (test.CharacterId, 3)), "added");
            Assert.That(Stored(removed)?.Owner, Is.EqualTo(0), "removed");
            Assert.That(Stored(unchanged), Is.EqualTo(((long, int)?) (test.CharacterId, 77)), "unchanged: not written again");
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A save whose transaction is rolled back did not store the row: the next save writes it, unchanged or not.
    [Test]
    public void RolledBackSave_NextSaveWritesIt() {
        Item item = test.Store(TestGameSession.Metadata(50000311), 2);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assume.That(db.SaveItems(test.CharacterId, item), Is.True);
        }
        item.Amount = 9;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.BeginTransaction();
            Assume.That(db.SaveItems(test.CharacterId, item), Is.True);
            db.Rollback();
        }
        Assume.That(Stored(item.Uid)?.Amount, Is.EqualTo(2));

        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, item), Is.True);
        }
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(9));

        // Committed now: the same values are not written again, a change is.
        SetStoredAmount(item.Uid, 77);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, item), Is.True);
        }
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(77));
        item.Amount = 10;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, item), Is.True);
        }
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(10));
    }

    // A load keeps the values each row was loaded with as stored: the first save after the login writes only the
    // rows changed since. Each loader (the bag, the equips, storage and furnishing opened later, a single item).
    [Test]
    public void LoginLoad_FirstSaveWritesOnlyChanged() {
        // In their bag slots, as a row the bag stored has them (a row without one gets one at the load, a change).
        Item[] bag = [test.Store(TestGameSession.Metadata(50000331), 2), test.Store(TestGameSession.Metadata(50000332), 2)];
        for (short slot = 0; slot < bag.Length; slot++) {
            bag[slot].Slot = slot;
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assume.That(db.SaveItems(test.CharacterId, bag), Is.True);
        }
        long kept = bag[0].Uid, changed = bag[1].Uid;
        long equipped = test.Store(TestGameSession.Metadata(50000333, EquipSlot.CP), 1, ItemGroup.Outfit, EquipSlot.CP).Uid;
        StartSaving();
        Assume.That(session.Item.Inventory.Get(kept), Is.Not.Null);
        Assume.That(session.Item.Equips.Get(equipped), Is.Not.Null);
        session.Item.Inventory.Get(changed)!.Amount = 5;
        SetStoredAmount(kept, 77);
        SetStoredAmount(changed, 77);
        SetStoredAmount(equipped, 77);
        Assert.That(session.SessionSave(), Is.True);

        Assert.Multiple(() => {
            Assert.That(Stored(kept)?.Amount, Is.EqualTo(77), "unchanged since the load: not written");
            Assert.That(Stored(equipped)?.Amount, Is.EqualTo(77), "equipped, unchanged: not written");
            Assert.That(Stored(changed)?.Amount, Is.EqualTo(5), "changed after the load: written");
            Assert.That(test.Errors, Is.Empty);
        });
    }

    private Item Loaded(Func<GameStorage.Request, IEnumerable<Item>> load, long uid) {
        using GameStorage.Request db = test.Storage.Context();
        return load(db).Single(item => item.Uid == uid);
    }

    [Test]
    public void LaterLoads_AreSeeded() {
        ItemMetadata metadata = TestGameSession.Metadata(50000334);
        test.Register(metadata);
        long storage, furnishing;
        using (GameStorage.Request db = test.Storage.Context()) {
            storage = db.CreateItem(test.AccountId, new Item(metadata, amount: 2))!.Uid;
            furnishing = db.CreateItem(test.AccountId, new Item(metadata, amount: 2) { Group = ItemGroup.Furnishing })!.Uid;
        }
        long single = test.Store(metadata, 2).Uid;
        (long Owner, Item Item)[] loads = [
            (test.AccountId, Loaded(db => db.GetStorage(test.AccountId), storage)),
            (test.AccountId, Loaded(db => db.GetItemGroups(test.AccountId, ItemGroup.Furnishing)[ItemGroup.Furnishing], furnishing)),
            (test.CharacterId, Loaded(db => [db.GetItem(single)!], single)),
        ];
        foreach ((long owner, Item item) in loads) {
            SetStoredAmount(item.Uid, 77);
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.SaveItems(owner, item), Is.True);
        }
        Assert.That(loads.Select(load => Stored(load.Item.Uid)?.Amount), Is.All.EqualTo(77));
    }

    // The account entered again on another channel while this session was still saving: the new channel claims the
    // session and loads (seeding the rows it read), then the old session's save comes, past or without the account
    // lock. It writes nothing; had it written, the new session's save would skip the row (its values unchanged since
    // its load) and leave the old session's amount: an item duplicated or lost. The row stays the new session's.
    [Test]
    public void SaveAfterAnotherEntry_WritesNothing() {
        Item item = test.Store(TestGameSession.Metadata(50000336), 2);
        StartSaving();
        Assume.That(session.SessionSave(), Is.True, "first save");
        session.Item.Inventory.Get(item.Uid)!.Amount = 5;

        Item loaded;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.BeginTransaction();
            db.ClaimSession(test.AccountId);
            loaded = db.GetItem(item.Uid)!;
            Assume.That(db.Commit(), Is.True);
        }
        Assume.That(loaded.Amount, Is.EqualTo(2));

        Assert.That(session.SessionSave(), Is.False, "the old session's save");
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(2), "not written by the old session");
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, loaded), Is.True);
        }
        Assert.Multiple(() => {
            Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(2), "the new session's save: its amount");
            Assert.That(test.Errors, Has.Some.Contains("entered again"));
        });
    }

    // Two stacks merged by the bag's sort: the session save deletes the emptied one's row (one conditional delete,
    // owner 0 and amount 0), and a row of that list that is not empty stays.
    [Test]
    public void MergedAwayStack_RowIsDeleted() {
        ItemMetadata metadata = TestGameSession.Metadata(50000337);
        long first = test.Store(metadata, 3).Uid, second = test.Store(metadata, 4).Uid;
        StartSaving();
        Item kept = session.Item.Inventory.Get(first)!;
        session.Item.Inventory.Sort(kept.Inventory, mergeStacks: true);
        Assert.That(session.SessionSave(), Is.True);

        (long, int)? firstRow = Stored(first), secondRow = Stored(second);
        Assert.Multiple(() => {
            Assert.That(new[] { firstRow, secondRow }.Count(row => row == null), Is.EqualTo(1), "the emptied stack's row");
            Assert.That(new[] { firstRow, secondRow }.Single(row => row != null), Is.EqualTo(((long, int)?) (test.CharacterId, 7)));
            Assert.That(test.Errors, Is.Empty);
        });
        using (GameStorage.Request db = test.Storage.Context()) {
            long left = test.Store(metadata, 2).Uid;
            Assert.That(db.DeleteEmptiedItems([left]), Is.EqualTo(0), "a row with an owner and an amount");
            Assert.That(Stored(left)?.Amount, Is.EqualTo(2));
        }
    }

    // A created item has no loaded values: its first save writes its row (as an item a mail or the market made).
    [Test]
    public void CreatedItem_FirstSaveWrites() {
        Item item = test.Store(TestGameSession.Metadata(50000335), 2);
        SetStoredAmount(item.Uid, 77);
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.SaveItems(test.CharacterId, item), Is.True);
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(2));
    }

    // A load in a transaction reads the transaction's own writes, which a rollback takes back: nothing is seeded, so
    // the item's save writes the amount it was loaded with (the row holds 2 again). Two rules keep it: no seed in a
    // transaction, and the rollback's forget of the row is a write after the load; either one alone keeps it.
    [Test]
    public void LoadInTransaction_IsNotSeeded() {
        Item item = test.Store(TestGameSession.Metadata(50000336), 2);
        Item loaded;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.BeginTransaction();
            item.Amount = 9;
            Assume.That(db.SaveItems(test.CharacterId, item), Is.True);
            loaded = db.GetInventory(test.CharacterId).Values.SelectMany(items => items).Single(row => row.Uid == item.Uid);
            db.Rollback();
        }
        Assume.That(Stored(item.Uid)?.Amount, Is.EqualTo(2));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, loaded), Is.True);
        }
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(9));
    }

    // A Find of a row the request already tracks gives the row as tracked, not as the DB holds it now (here changed
    // behind it): nothing is seeded, and the item's save writes its amount.
    [Test]
    public void LoadOfATrackedRow_IsNotSeeded() {
        Item item = test.Store(TestGameSession.Metadata(50000337), 2);
        Item loaded;
        using (GameStorage.Request db = test.Storage.Context()) {
            item.Amount = 6;
            Assume.That(db.UpdateItem(item), Is.True); // the row stays tracked at 6
            SetStoredAmount(item.Uid, 77);
            loaded = db.GetItem(item.Uid)!;
        }
        Assume.That(loaded.Amount, Is.EqualTo(6));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, loaded), Is.True);
        }
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(6));
    }

    // A session that did not take the account lock at its entry (refused after its tries, or the world down) holds
    // no rows: the channel it came from may still be storing its save, which this server's stamps never see. No load
    // is seeded, and the first save writes every row, as before the seeds (the rows hold the server's amounts again).
    [Test]
    public void LoadOfAnOwnerNotHeld_IsNotSeeded() {
        held!.Dispose();
        ItemMetadata metadata = TestGameSession.Metadata(50000339);
        test.Register(metadata);
        long storage;
        using (GameStorage.Request db = test.Storage.Context()) {
            storage = db.CreateItem(test.AccountId, new Item(metadata, amount: 2))!.Uid;
        }
        long bag = test.Store(metadata, 2).Uid, single = test.Store(metadata, 2).Uid;
        (long Owner, Item Item)[] loads = [
            (test.CharacterId, Loaded(db => db.GetInventory(test.CharacterId).Values.SelectMany(items => items), bag)),
            (test.AccountId, Loaded(db => db.GetStorage(test.AccountId), storage)),
            (test.CharacterId, Loaded(db => [db.GetItem(single)!], single)),
        ];
        foreach ((long owner, Item item) in loads) {
            SetStoredAmount(item.Uid, 77); // the other server's save, after the load
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.SaveItems(owner, item), Is.True);
        }
        Assert.That(loads.Select(load => Stored(load.Item.Uid)?.Amount), Is.All.EqualTo(2));
    }

    // A write of a row between the load's query and its seeds (a split of another object of the row, here 10 to 7):
    // the query read the row before it, so its values are not the DB's; the row's stamp is past the clock read
    // before the query, and the row is not seeded. Its save writes 10.
    [Test]
    public void WriteDuringLoad_IsNotSeeded() {
        Item item = test.Store(TestGameSession.Metadata(50000340), 10);
        Item? piece = null;
        GameStorage.AfterSeedingQuery = () => {
            GameStorage.AfterSeedingQuery = null;
            using GameStorage.Request split = test.Storage.Context();
            piece = split.SplitItem(test.CharacterId, item, 3);
        };
        Item loaded = Loaded(db => db.GetInventory(test.CharacterId).Values.SelectMany(items => items), item.Uid);
        Assume.That(piece, Is.Not.Null);
        Assume.That(loaded.Amount, Is.EqualTo(10), "read before the split");
        Assume.That(Stored(item.Uid)?.Amount, Is.EqualTo(7));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, loaded), Is.True);
        }
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(10));
    }

    // A row written by another write after the load (a split of another object of the row) is not skipped: its
    // seed no longer counts.
    [Test]
    public void WriteAfterLoad_ForgetsTheSeed() {
        Item item = test.Store(TestGameSession.Metadata(50000338), 10);
        Item loaded = Loaded(db => db.GetInventory(test.CharacterId).Values.SelectMany(items => items), item.Uid);
        Item? piece;
        using (GameStorage.Request db = test.Storage.Context()) {
            piece = db.SplitItem(test.CharacterId, item, 3);
        }
        Assume.That(piece, Is.Not.Null);
        Assume.That(Stored(item.Uid)?.Amount, Is.EqualTo(7));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, loaded), Is.True); // loaded.Amount is 10, as loaded
        }
        Assert.That(Stored(item.Uid)?.Amount, Is.EqualTo(10));
    }
}

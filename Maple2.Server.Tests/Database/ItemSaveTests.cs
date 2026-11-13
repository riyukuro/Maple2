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
    }

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
}

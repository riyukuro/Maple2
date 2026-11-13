using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Database;

// A session's save stores the player row (its currency) and its components together or not at all, and a purchase
// stores the currency and the purchase's rows together: a part that fails rolls the whole save back, so the stored
// mesos and items stay in step. The session keeps its state and the next save stores it. The in-memory store has no
// transactions: the tests need MS2_TEST_GAME_DB, the connection string of a scratch MySQL game database.
public class SessionSaveTransactionTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int ItemId = 50000102;

    private readonly CommandHook hook = new();
    private DbContextOptions plain = null!;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("transactions need a real database: set MS2_TEST_GAME_DB");
        }
        hook.Before = null;
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(hook).Options);
        test.Register(TestGameSession.Metadata(ItemId));
        test.Start();
        // The managers the test session leaves bare that a full save writes (as DailyReviveCountTests loads the config).
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
        // The test session saves cleanly, so a test's refused write is the save's one failure.
        session.SessionSave();
        Assume.That(test.Errors, Is.Empty);
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null) {
            return;
        }
        hook.Before = null;
        using var context = new Ms2Context(plain);
        context.Mail.Where(mail => mail.ReceiverId == test.CharacterId).ExecuteDelete();
        context.Item.Where(item => item.OwnerId == test.CharacterId).ExecuteDelete();
        context.ItemStorage.Where(info => info.AccountId == test.AccountId).ExecuteDelete();
    }

    private long StoredMeso() {
        using var context = new Ms2Context(plain);
        return context.Character.AsNoTracking().Single(character => character.Id == test.CharacterId).Currency.Meso;
    }

    private int StoredAmount(long uid) {
        using var context = new Ms2Context(plain);
        return context.Item.AsNoTracking().Single(item => item.Id == uid).Amount;
    }

    private long[] Mails() {
        using var context = new Ms2Context(plain);
        return context.Mail.Where(mail => mail.ReceiverId == test.CharacterId).Select(mail => mail.Id).ToArray();
    }

    // Refuses the first command that holds text, as a database that refuses one write would.
    private void RefuseOnce(string text) {
        bool refused = false;
        hook.Before = command => {
            if (!refused && command.CommandText.Contains(text)) {
                refused = true;
                throw new InvalidOperationException("write refused");
            }
        };
    }

    // The inventory's save fails after the player row was written: the new mesos are not stored either (they used to
    // be stored without the items).
    [Test]
    public void Save_ItemsNotSaved_StoresNoCurrency() {
        Item item = test.Store(TestGameSession.Metadata(ItemId), 3);
        Assert.That(session.Item.Inventory.Add(item), Is.True);
        session.Currency.Meso = 4321;
        RefuseOnce("UPDATE `item`");

        session.SessionSave();
        hook.Before = null;

        Assert.That(StoredMeso(), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Not.Empty);

        // The session kept its state, and the next save stores it.
        test.Errors.Clear();
        session.SessionSave();
        Assert.That(StoredMeso(), Is.EqualTo(4321), string.Join("\n", test.Errors));
    }

    // The player row is not saved: nothing else is (the components' saves used to be stored, with the player row
    // the request still had pending).
    [Test]
    public void Save_PlayerNotSaved_StoresNoItems() {
        Item item = test.Store(TestGameSession.Metadata(ItemId), 3);
        Assert.That(session.Item.Inventory.Add(item), Is.True);
        session.SessionSave();
        Assert.That(StoredAmount(item.Uid), Is.EqualTo(3));
        item.Amount = 7;
        session.Currency.Meso = 4321;
        RefuseOnce("UPDATE `account`");

        session.SessionSave();
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(StoredAmount(item.Uid), Is.EqualTo(3));
            Assert.That(StoredMeso(), Is.EqualTo(0));
        });
    }

    // The purchase's currency is not saved: its rows are not stored (they used to be, with the currency the request
    // still had pending).
    [Test]
    public void SavePurchase_CurrencyNotSaved_StoresNoRows() {
        session.Currency.Meso = 4321;
        RefuseOnce("UPDATE `account`");

        session.SavePurchase(db => db.CreateMail(new Mail { ReceiverId = test.CharacterId }));
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(Mails(), Is.Empty);
            Assert.That(StoredMeso(), Is.EqualTo(0));
        });
    }

    // The purchase's rows are not saved: the currency is not stored either (it used to be, without the rows).
    [Test]
    public void SavePurchase_RowsNotSaved_StoresNoCurrency() {
        session.Currency.Meso = 4321;
        RefuseOnce("INSERT INTO `mail`");

        session.SavePurchase(db => db.CreateMail(new Mail { ReceiverId = test.CharacterId }));
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(Mails(), Is.Empty);
            Assert.That(StoredMeso(), Is.EqualTo(0));
        });
    }

    [Test]
    public void SavePurchase_Saved_StoresBoth() {
        session.Currency.Meso = 4321;

        session.SavePurchase(db => db.CreateMail(new Mail { ReceiverId = test.CharacterId }));

        Assert.Multiple(() => {
            Assert.That(Mails(), Has.Length.EqualTo(1));
            Assert.That(StoredMeso(), Is.EqualTo(4321));
        });
    }

    // A storage meso move whose storage row is refused stores no currency either (the bag's mesos used to be stored
    // by the next save while the storage kept its old amount).
    [Test]
    public void StorageDeposit_StorageNotSaved_StoresNoCurrency() {
        session.Currency.Meso = 5000;
        session.SessionSave();
        using var storage = new StorageManager(session);
        RefuseOnce("INSERT INTO `item-storage`");

        storage.DepositMesos(1200);
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(StoredMeso(), Is.EqualTo(5000));
        });
    }

    private sealed class CommandHook : DbCommandInterceptor {
        public Action<DbCommand>? Before;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) {
            Before?.Invoke(command);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                                                                          InterceptionResult<DbDataReader> result) {
            Before?.Invoke(command);
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
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
    private const int RewardItemId = 50000103;
    private const int LapenshardId = 41000001;
    private const int QuestId = 91000999;

    private readonly CommandHook hook = new();
    private readonly CommitHook commit = new();
    private DbContextOptions plain = null!;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("transactions need a real database: set MS2_TEST_GAME_DB");
        }
        hook.Before = null;
        commit.Fail = false;
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(hook, commit).Options);
        test.Register(TestGameSession.Metadata(ItemId));
        test.Register(TestGameSession.Metadata(RewardItemId));
        test.Register(TestGameSession.Metadata(LapenshardId));
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
        commit.Fail = false;
        using var context = new Ms2Context(plain);
        context.Quest.Where(quest => quest.OwnerId == test.CharacterId && quest.Id == QuestId).ExecuteDelete();
        context.DungeonRecord.Where(record => record.OwnerId == test.CharacterId).ExecuteDelete();
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

        SaveOnce();
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

        SaveOnce();
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

    // Refuses every command that holds one of the texts, as a database that refuses one row on every try would.
    private void RefuseAlways(params string[] texts) {
        hook.Before = command => {
            if (texts.Any(command.CommandText.Contains)) {
                throw new InvalidOperationException("write refused");
            }
        };
    }

    private IDictionary<LapenshardSlot, int> StoredLapenshards() {
        using var context = new Ms2Context(plain);
        return context.CharacterConfig.AsNoTracking().Single(config => config.CharacterId == test.CharacterId).Lapenshards
            ?? new Dictionary<LapenshardSlot, int>();
    }

    private int StoredItems(int itemId) {
        using var context = new Ms2Context(plain);
        return context.Item.AsNoTracking().Where(item => item.OwnerId == test.CharacterId && item.ItemId == itemId).Sum(item => item.Amount);
    }

    private QuestState? StoredQuest(int questId) {
        using var context = new Ms2Context(plain);
        return context.Quest.AsNoTracking().Where(quest => quest.OwnerId == test.CharacterId && quest.Id == questId)
            .Select(quest => (QuestState?) quest.State).SingleOrDefault();
    }

    // One try of the save, without the session save's second try and end: what one failed save stores.
    private bool SaveOnce() => (bool) typeof(GameSession).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(TimeSpan)])!.Invoke(session, [TimeSpan.FromSeconds(60)])!;
    private bool Ended => (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)! == 1;

    // A completion whose quest row is refused pays nothing: the reward items get their rows at once, so the completed
    // quest row is stored first, and a refused row leaves the quest open with no reward stored or held. It used to be
    // stored only by the next session save, after the reward rows, so a failed save gave the reward again.
    [Test]
    public void Save_QuestRowRefused_RewardNotClaimableTwice() {
        test.EnterMap(TestGameSession.Map(2000023));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        QuestMetadata data = TestGameSession.QuestData(QuestId);
        data = data with { CompleteReward = data.CompleteReward with { EssentialItem = [new QuestMetadataReward.Item(RewardItemId, 1, 2)] } };
        test.Register(data);
        Quest quest = test.StartQuest(data);
        session.SessionSave();
        Assume.That((StoredQuest(QuestId), test.Errors.Count), Is.EqualTo(((QuestState?) QuestState.Started, 0)));
        RefuseAlways("UPDATE `quest`");

        Assert.That(session.Quest.Complete(quest), Is.False);
        SaveOnce();
        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(session.Item.Inventory.Find(RewardItemId).Sum(item => item.Amount), Is.EqualTo(0));
            Assert.That(StoredQuest(QuestId), Is.EqualTo(QuestState.Started));
            Assert.That(StoredItems(RewardItemId), Is.EqualTo(0));
        });

        // The database takes the row again: the completion is stored, then paid once, and not again.
        hook.Before = null;
        Assert.That(session.Quest.Complete(quest), Is.True);
        Assert.That((StoredQuest(QuestId), StoredItems(RewardItemId)), Is.EqualTo(((QuestState?) QuestState.Completed, 2)));
        Assert.That(session.Quest.Complete(quest), Is.False);
        session.SessionSave();
        Assert.That((StoredQuest(QuestId), StoredItems(RewardItemId)), Is.EqualTo(((QuestState?) QuestState.Completed, 2)));
    }

    // A lapenshard taken off while the config row is refused stays worn and gets no item row: the config without the
    // shard and the shard's item row are stored together. The item row used to be written at once and the config by
    // the next session save, so a failed save left the shard both worn and in the bag after the next login.
    [Test]
    public void Save_ConfigRowRefused_NoLapenshardDuplicate() {
        test.EnterMap(TestGameSession.Map(2000023));
        session.Config.SetLapenshard(LapenshardSlot.Red1, LapenshardId);
        session.SessionSave();
        Assume.That(StoredLapenshards(), Does.ContainKey(LapenshardSlot.Red1));
        RefuseAlways("UPDATE `character-config`");

        Assert.That(session.Config.UnequipLapenshard(LapenshardSlot.Red1), Is.False);
        SaveOnce();
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(LapenshardId).Count(), Is.EqualTo(0));
            Assert.That(StoredLapenshards()[LapenshardSlot.Red1], Is.EqualTo(LapenshardId));
            Assert.That(StoredItems(LapenshardId), Is.EqualTo(0));
        });

        hook.Before = null;
        Assert.That(session.Config.UnequipLapenshard(LapenshardSlot.Red1), Is.True);
        Assert.That((StoredLapenshards().ContainsKey(LapenshardSlot.Red1), StoredItems(LapenshardId)), Is.EqualTo((false, 1)));
        Assert.That(session.Item.Inventory.Find(LapenshardId).Count(), Is.EqualTo(1));
    }

    // A save refused once (a passing error such as a deadlock) is tried again at once, and the second try stores it.
    [Test]
    public void SessionSave_RefusedOnce_SecondTryStores() {
        session.Currency.Meso = 4321;
        session.Config.InstantReviveCount += 1;
        RefuseOnce("UPDATE `character-config`");

        Assert.That(session.SessionSave(), Is.True);
        Assert.That((Ended, StoredMeso()), Is.EqualTo((false, 4321L)));
    }

    // A save whose second try fails too ends the session: the stored state is the last full save, and the session's
    // own memory is not written in part. It used to play on until the third failed save in a row, while the items it
    // made were stored at once and the items it used up were not.
    [Test]
    public void SessionSave_RefusedTwice_EndsTheSession() {
        session.Currency.Meso = 4321;
        session.Config.InstantReviveCount += 1;
        RefuseAlways("UPDATE `character-config`");

        Assert.That(session.SessionSave(), Is.False);
        Assert.Multiple(() => {
            Assert.That(Ended, Is.True);
            Assert.That(StoredMeso(), Is.EqualTo(0));
            Assert.That(session.Currency.Meso, Is.EqualTo(4321));
        });
    }

    // A save that throws out of its transaction (a deadlock on the commit) is a failed save too.
    [Test]
    public void SessionSave_CommitThrows_EndsTheSession() {
        session.Currency.Meso = 4321;
        commit.Fail = true;

        Assert.That(session.SessionSave(), Is.False);
        Assert.That((Ended, StoredMeso()), Is.EqualTo((true, 0L)));
    }

    // The entry's save is a gate: a character whose dungeon row the database refuses on each try is not let in.
    // The entry is refused (EnterServer answers false and the connection is closed by its caller) and nothing of the
    // character's state is stored.
    [Test]
    public void EntrySave_DungeonRowRefused_RefusesTheEntry() {
        using (GameStorage.Request db = test.Storage.Context()) {
            DungeonRecord? record = db.CreateDungeonRecord(new DungeonRecord(99000001), test.CharacterId);
            Assume.That(record, Is.Not.Null);
            session.Dungeon.Records[record!.DungeonId] = record;
        }
        Assume.That(session.EntrySave(), Is.True);
        session.Currency.Meso = 4321;
        RefuseAlways("UPDATE `dungeon-record`");

        Assert.That(session.EntrySave(), Is.False);
        Assert.That(StoredMeso(), Is.EqualTo(0));

        hook.Before = null;
        Assert.That(session.EntrySave(), Is.True);
        Assert.That(StoredMeso(), Is.EqualTo(4321));
    }

    // The timed save runs on the field thread; a failed one is tried again about 2 s later on the field queue (the
    // field thread does not wait), and the session is ended when that fails too. While the session changes field or
    // channel it does not run (that move saves on its own).
    [Test]
    public void TimedSave_Refused_EndsTheSession() {
        test.EnterMap(TestGameSession.Map(2000023));
        AllowMigrationSave();
        session.Currency.Meso = 4321;
        session.Config.InstantReviveCount += 1;
        RefuseAlways("UPDATE `character-config`");

        session.State = SessionState.ChangeMap;
        session.TimedSaveOnField(session.Field!);
        Assert.That((Ended, test.Errors.Count), Is.EqualTo((false, 0)));

        session.State = SessionState.Connected;
        int queued = session.Field!.Scheduler.Count;
        session.TimedSaveOnField(session.Field!);
        Assert.That((Ended, StoredMeso(), session.Field!.Scheduler.Count), Is.EqualTo((false, 0L, queued + 1)));

        session.TimedSaveRetry(session.Field!);
        Assert.That((Ended, StoredMeso()), Is.EqualTo((true, 0L)));
    }

    // A timed save refused once is stored by the try 2 s later, and the session plays on.
    [Test]
    public void TimedSave_RefusedOnce_RetryStores() {
        test.EnterMap(TestGameSession.Map(2000023));
        AllowMigrationSave();
        session.State = SessionState.Connected;
        session.Currency.Meso = 4321;
        session.Config.InstantReviveCount += 1;
        RefuseOnce("UPDATE `character-config`");

        session.TimedSaveOnField(session.Field!);
        Assert.That((Ended, StoredMeso()), Is.EqualTo((false, 0L)));

        session.TimedSaveRetry(session.Field!);
        Assert.That((Ended, StoredMeso()), Is.EqualTo((false, 4321L)));
    }

    // Once a move made its MigrationSave, no timed save (nor its retry) stores anything: the other channel may have
    // loaded the player and saved it by then. A move refused after it (MigrationRefused, the session plays on here)
    // saves again.
    [Test]
    public void TimedSave_AfterMigrationSave_StoresNothing() {
        test.EnterMap(TestGameSession.Map(2000023));
        AllowMigrationSave();
        session.State = SessionState.Connected;
        session.MigrationSave();
        session.Currency.Meso = 4321;

        session.TimedSaveOnField(session.Field!);
        session.TimedSaveRetry(session.Field!);
        Assert.That((Ended, StoredMeso()), Is.EqualTo((false, 0L)));

        session.MigrationRefused();
        session.TimedSaveOnField(session.Field!);
        Assert.That((Ended, StoredMeso()), Is.EqualTo((false, 4321L)));
    }

    // A repeatable quest started again stores its started row before its accept items are given: refused, the quest
    // stays completed and nothing is given. The started row used to be left to the next session save while the accept
    // items got their rows at once, so a failed save let the quest be started again and the items given again.
    [Test]
    public void Start_QuestRowRefused_AcceptItemsNotGivenTwice() {
        test.EnterMap(TestGameSession.Map(2000023));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        QuestMetadata data = TestGameSession.QuestData(QuestId);
        data = data with {
            Basic = data.Basic with { Repeat = 1 },
            AcceptReward = data.AcceptReward with { EssentialItem = [new QuestMetadataReward.Item(RewardItemId, 1, 2)] },
        };
        test.Register(data);
        Quest quest = test.StartQuest(data);
        Assume.That(session.Quest.Complete(quest), Is.True);
        Assume.That((StoredQuest(QuestId), StoredItems(RewardItemId)), Is.EqualTo(((QuestState?) QuestState.Completed, 0)));
        RefuseAlways("UPDATE `quest`");

        Assert.That(session.Quest.Start(QuestId, true), Is.EqualTo(QuestError.s_quest_error_accept_fail));
        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Item.Inventory.Find(RewardItemId).Sum(item => item.Amount), Is.EqualTo(0));
            Assert.That(StoredQuest(QuestId), Is.EqualTo(QuestState.Completed));
            Assert.That(StoredItems(RewardItemId), Is.EqualTo(0));
        });

        hook.Before = null;
        Assert.That(session.Quest.Start(QuestId, true), Is.EqualTo(QuestError.none));
        Assert.That((StoredQuest(QuestId), StoredItems(RewardItemId)), Is.EqualTo(((QuestState?) QuestState.Started, 2)));
        Assert.That(session.Quest.Start(QuestId, true), Is.EqualTo(QuestError.s_quest_error_accept_fail));
        Assert.That(StoredItems(RewardItemId), Is.EqualTo(2));
    }

    // A lapenshard put on stores the config with the shard worn and the bag without it together: refused, the shard
    // stays in the bag and is not worn. The bag's change used to be left to the next session save, so an equip and an
    // unequip with no save between left two shards after the next login.
    [Test]
    public void EquipLapenshard_ConfigRowRefused_NoLapenshardDuplicate() {
        test.EnterMap(TestGameSession.Map(2000023));
        Item shard = test.Store(TestGameSession.Metadata(LapenshardId), 1);
        Assert.That(session.Item.Inventory.Add(shard), Is.True);
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(StoredItems(LapenshardId), Is.EqualTo(1));
        RefuseAlways("UPDATE `character-config`");

        Assert.That(session.Config.EquipLapenshard(shard.Uid, LapenshardSlot.Red1), Is.False);
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(LapenshardId).Count(), Is.EqualTo(1));
            Assert.That(StoredLapenshards(), Does.Not.ContainKey(LapenshardSlot.Red1));
            Assert.That(StoredItems(LapenshardId), Is.EqualTo(1));
        });

        hook.Before = null;
        Assert.That(session.Config.EquipLapenshard(shard.Uid, LapenshardSlot.Red1), Is.True);
        Assert.That((StoredLapenshards()[LapenshardSlot.Red1], StoredItems(LapenshardId)), Is.EqualTo((LapenshardId, 0)));
        Assert.That(session.Config.UnequipLapenshard(LapenshardSlot.Red1), Is.True);
        Assert.That((StoredLapenshards().ContainsKey(LapenshardSlot.Red1), StoredItems(LapenshardId)), Is.EqualTo((false, 1)));
    }

    private int DeleteEntries(long uid) =>
        ((List<Item>) typeof(Maple2.Server.Game.Manager.Items.InventoryManager).GetField("delete", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Item.Inventory)!).Count(item => item.Uid == uid);

    // A use stored with its own rows (SaveConsumed in the use's transaction, ConsumeStored once it committed): the
    // stack's row holds the lower amount once, and the session save writes the same amount again, not a second
    // decrement; the last ones given up have their row at owner 0 already and are not put on the delete list.
    [Test]
    public void SaveConsumed_ThenConsumeStored_RowsMatchTheBag() {
        test.EnterMap(TestGameSession.Map(2000023));
        Item stack = test.Store(TestGameSession.Metadata(ItemId), 3);
        Assert.That(session.Item.Inventory.Add(stack), Is.True);
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(StoredItems(ItemId), Is.EqualTo(3));

        Assert.That(session.TrySavePurchase(db => session.Item.Inventory.SaveConsumed(db, stack.Uid, 1)), Is.True);
        Assert.That((StoredItems(ItemId), session.Item.Inventory.Find(ItemId).Sum(item => item.Amount)), Is.EqualTo((2, 3)));
        Assert.That(session.Item.Inventory.ConsumeStored(stack.Uid, 1), Is.True);
        Assert.That(session.SessionSave(), Is.True);
        Assert.That((StoredItems(ItemId), session.Item.Inventory.Find(ItemId).Sum(item => item.Amount)), Is.EqualTo((2, 2)));

        Assert.That(session.TrySavePurchase(db => session.Item.Inventory.SaveConsumed(db, stack.Uid, 2)), Is.True);
        Assert.That(StoredItems(ItemId), Is.EqualTo(0));
        Assert.That(session.Item.Inventory.ConsumeStored(stack.Uid, 2), Is.True);
        Assert.That((DeleteEntries(stack.Uid), session.Item.Inventory.Find(ItemId).Count()), Is.EqualTo((0, 0)));
        Assert.That(session.SessionSave(), Is.True);
        Assert.That((StoredItems(ItemId), DeleteEntries(stack.Uid)), Is.EqualTo((0, 0)));
    }

    // The test session skips the save at Dispose; these tests make the one MigrationSave runs.
    private void AllowMigrationSave() {
        typeof(GameSession).GetField("preMigrationSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, false);
    }

    // The save at logout (Dispose saves through MigrationSave) with a progress row refused stores nothing: no money
    // without the progress it was paid with.
    [Test]
    public void MigrationSave_ProgressPartRefused_StoresNothing() {
        session.Currency.Meso = 4321;
        session.Config.InstantReviveCount += 1;
        RefuseAlways("UPDATE `character-config`");

        AllowMigrationSave();
        session.MigrationSave();

        Assert.That(StoredMeso(), Is.EqualTo(0));
    }

    // A failed save before a migration is not tried again (at Dispose): the client was already sent to the other
    // channel, which may have loaded the player by then.
    [Test]
    public void MigrationSave_Failed_IsNotTriedAgain() {
        session.Currency.Meso = 4321;
        RefuseOnce("UPDATE `account`");

        AllowMigrationSave();
        session.MigrationSave();
        hook.Before = null;
        session.MigrationSave();

        Assert.That(StoredMeso(), Is.EqualTo(0));
    }

    // A rolled-back save puts the row versions back, so the next save starts without a conflict.
    [Test]
    public void Save_RolledBack_KeepsRowVersions() {
        Item item = test.Store(TestGameSession.Metadata(ItemId), 3);
        Assert.That(session.Item.Inventory.Add(item), Is.True);
        DateTime account = session.Player.Value.Account.LastModified;
        DateTime character = session.Player.Value.Character.LastModified;
        session.Currency.Meso = 4321;
        RefuseOnce("UPDATE `item`");

        SaveOnce();
        hook.Before = null;

        Assert.That((session.Player.Value.Account.LastModified, session.Player.Value.Character.LastModified), Is.EqualTo((account, character)));
    }

    // A purchase whose rows are refused puts the row versions SaveCurrency moved back.
    [Test]
    public void SavePurchase_RolledBack_KeepsRowVersions() {
        DateTime account = session.Player.Value.Account.LastModified;
        DateTime character = session.Player.Value.Character.LastModified;
        session.Currency.Meso = 4321;
        RefuseOnce("INSERT INTO `mail`");

        session.SavePurchase(db => db.CreateMail(new Mail { ReceiverId = test.CharacterId }));
        hook.Before = null;

        Assert.That((session.Player.Value.Account.LastModified, session.Player.Value.Character.LastModified), Is.EqualTo((account, character)));
    }

    private sealed class CommitHook : DbTransactionInterceptor {
        public bool Fail;

        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) {
            if (Fail) {
                throw new InvalidOperationException("commit refused");
            }
            return result;
        }
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

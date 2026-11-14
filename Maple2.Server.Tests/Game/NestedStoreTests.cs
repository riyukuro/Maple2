using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A store started inside another store of the same session (GameSession.NestedStore) is refused: the World's account
// lock is not re-entrant, so it waited on the lock its own thread held and its release freed the outer store's lock.
public class NestedStoreTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly List<string> nested = [];

    [SetUp]
    public void Live() {
        test = new TestGameSession();
        test.Start();
        test.SavingConfig(session);
        typeof(GameSession).GetField("preMigrationSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, false);
        session.State = SessionState.Connected;
        test.TakeSent();
        nested.Clear();
        // Nested on purpose here: recorded by this test, not failed by NestedStoreCheck.
        GameSession.OnNestedStore = (where, _) => nested.Add(where);
    }

    [Test]
    public void APurchaseInsideAPurchase_IsRefused_TheOuterOneStores() {
        int acquired = test.World.Acquired, released = test.World.Released;
        bool? inner = null;

        bool outer = session.TrySavePurchase(_ => {
            inner = session.TrySavePurchase(_ => true);
            return true;
        });

        Assert.Multiple(() => {
            Assert.That(outer, Is.True);
            Assert.That(inner, Is.False);
            Assert.That(nested, Is.EqualTo(new[] { "SavePurchase" }));
            Assert.That(test.World.Acquired - acquired, Is.EqualTo(1), "the lock is taken once");
            Assert.That(test.World.Released - released, Is.EqualTo(1), "and given back once, by the outer store");
            Assert.That(test.Errors, Has.Some.Contains("inside another store"));
        });
    }

    // A session save inside a store is deferred, not refused: it runs once the outer store gave its lock back, and
    // the session goes on (a nesting path not found yet would otherwise disconnect the player).
    [Test]
    public void ASessionSaveInsideAPurchase_IsDeferred_ToAfterTheOuterStore() {
        int acquired = test.World.Acquired, released = test.World.Released;
        bool? inner = null;
        int releasedBeforeSave = -1;

        Assert.That(session.TrySavePurchase(_ => {
            inner = session.SessionSave();
            test.World.OnAcquire = () => releasedBeforeSave = test.World.Released - released;
            return true;
        }), Is.True);
        Assert.Multiple(() => {
            Assert.That(inner, Is.True);
            Assert.That(nested, Is.EqualTo(new[] { "SessionSave deferred" }));
            Assert.That(releasedBeforeSave, Is.EqualTo(1), "the deferred save takes the lock after the outer store gave it back");
            Assert.That((test.World.Acquired - acquired, test.World.Released - released), Is.EqualTo((2, 2)));
            Assert.That(test.Errors, Has.Some.Contains("SessionSave deferred"));
            Assert.That(Ended(), Is.False);
        });
    }

    // The deferred save ends the session when it fails, as any failed session save does.
    [Test]
    public void ADeferredSessionSaveThatFails_EndsTheSession() {
        Assert.That(session.TrySavePurchase(_ => {
            session.SessionSave();
            test.FailSave = _ => true;
            return true;
        }), Is.True);
        Assert.Multiple(() => {
            Assert.That(nested, Is.EqualTo(new[] { "SessionSave deferred" }));
            Assert.That(Ended(), Is.True);
        });
    }

    // A store of this session inside another session's store inside one of this session's: nested too.
    [Test]
    public void ANestingThroughAnotherSession_IsRefused() {
        var other = new TestGameSession();
        GameSession otherSession = other.Start();
        other.SavingConfig(otherSession);
        bool? inner = null, middle = null;

        Assert.That(session.TrySavePurchase(_ => {
            middle = otherSession.TrySavePurchase(_ => {
                inner = session.TrySavePurchase(_ => true);
                return true;
            });
            return true;
        }), Is.True);
        Assert.Multiple(() => {
            Assert.That(middle, Is.True);
            Assert.That(inner, Is.False);
            Assert.That(nested, Is.EqualTo(new[] { "SavePurchase" }));
        });
    }

    // A lock another holder has (refused after the tries) refuses the store and is not given back: that holder's
    // lock stays.
    [Test]
    public void ALockHeldElsewhere_RefusesTheStore_AndIsNotReleased() {
        test.World.LockError = "held";
        int released = test.World.Released;
        bool rowsRan = false;

        bool purchase = session.TrySavePurchase(_ => rowsRan = true);
        bool save = session.SessionSave();

        Assert.Multiple(() => {
            Assert.That(purchase, Is.False);
            Assert.That(rowsRan, Is.False, "nothing is stored");
            Assert.That(save, Is.False);
            Assert.That(Ended(), Is.True, "a session save refused ends the session");
            Assert.That(test.World.Released, Is.EqualTo(released), "ReleaseLock is not called");
            Assert.That(test.Errors, Has.Some.Contains("held elsewhere"));
            Assert.That(nested, Is.Empty);
        });
    }

    private bool Ended() =>
        (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)! == 1;

    [Test]
    public void AStoreAfterAStore_IsNotNested() {
        Assert.That(session.TrySavePurchase(_ => true), Is.True);
        Assert.That(session.TrySavePurchase(_ => true), Is.True);
        Assert.That(session.SessionSave(), Is.True);
        Assert.That(nested, Is.Empty);
    }

    // A refused purchase is a failed one for its caller: its undo runs.
    [Test]
    public void ARefusedNestedPurchase_RunsItsUndo() {
        bool undone = false;

        Assert.That(session.TrySavePurchase(_ => {
            session.TrySavePurchase(_ => true, () => undone = true);
            return true;
        }), Is.True);
        Assert.That((undone, nested.Count), Is.EqualTo((true, 1)));
    }

    // A store queued by AfterStore inside a store runs once the outer one gave its lock back: not nested.
    [Test]
    public void AStoreAfterStoreQueued_RunsAfterTheOuterStore_NotNested() {
        int acquired = test.World.Acquired, released = test.World.Released;
        bool? inner = null;

        Assert.That(session.TrySavePurchase(_ => {
            session.AfterStore(() => inner = session.TrySavePurchase(_ => true));
            return true;
        }), Is.True);
        Assert.Multiple(() => {
            Assert.That(inner, Is.True);
            Assert.That(nested, Is.Empty);
            Assert.That((test.World.Acquired - acquired, test.World.Released - released), Is.EqualTo((2, 2)));
        });
    }

    // Another session's store inside this one's takes another account's lock: not nested.
    [Test]
    public void AnotherSessionsStoreInside_IsNotNested() {
        var other = new TestGameSession();
        GameSession otherSession = other.Start();
        other.SavingConfig(otherSession);
        bool? inner = null;

        Assert.That(session.TrySavePurchase(_ => {
            inner = otherSession.TrySavePurchase(_ => true);
            return true;
        }), Is.True);
        Assert.That(inner, Is.True);
        Assert.That(nested, Is.Empty);
    }

    // The last of a stack taken in a store's AfterCommit (the paid materials of a craft, a dismantle, an upgrade:
    // ConsumeStored) counts item_destroy, and a field mission that fills completes with a store of its own. It is
    // counted once the outer store gave its lock back (AfterStore): it completes, nothing nests. It used to count
    // inside the store, where the completion's store was refused and the mission stayed filled and not completed.
    [Test]
    public void TheLastOfAStackTakenAfterCommit_CompletesItsMissionAfterTheStore() {
        const int itemId = 50000102;
        test.EnterMap(TestGameSession.Map(2000023));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        Quest quest = test.StartQuest(TestGameSession.QuestData(990801, QuestType.FieldMission) with {
            Conditions = [new ConditionMetadata(ConditionType.item_destroy, 1, new ConditionMetadata.Parameters(Integers: [itemId]), null)],
        });
        Item stack = test.Store(TestGameSession.Metadata(itemId), 2);
        Assert.That(session.Item.Inventory.Add(stack), Is.True);
        int conditionAtCommit = -1;

        Assert.That(session.TrySavePurchase(db => {
            db.AfterCommit(() => {
                session.Item.Inventory.ConsumeStored(stack.Uid, 2);
                conditionAtCommit = quest.Conditions[0].Counter;
            });
            return true;
        }), Is.True);

        Assert.Multiple(() => {
            Assert.That(nested, Is.Empty);
            Assert.That(conditionAtCommit, Is.EqualTo(0), "counted after the store, not inside it");
            Assert.That((quest.Conditions[0].Counter, quest.State, quest.CompletionCount), Is.EqualTo((1, QuestState.Completed, 1)));
        });
    }

    // Outside a store the count is at once.
    [Test]
    public void TheLastOfAStackTakenOutsideAStore_CountsAtOnce() {
        const int itemId = 50000102;
        Quest quest = test.StartQuest(TestGameSession.QuestData(990802, QuestType.EpicQuest) with {
            Conditions = [new ConditionMetadata(ConditionType.item_destroy, 5, new ConditionMetadata.Parameters(Integers: [itemId]), null)],
        });
        Item stack = test.Store(TestGameSession.Metadata(itemId), 1);
        Assert.That(session.Item.Inventory.Add(stack), Is.True);

        Assert.That(session.Item.Inventory.ConsumeStored(stack.Uid, 1), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    private void CompletionTables() {
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
    }

    // An event gift goes to the bag in the claim store's AfterCommit (EventClaim.Give); a field mission that counts
    // the item held (item_exist) fills there. It completes once the claim store gave its lock back
    // (QuestManager.CompleteAutomatic through AfterStore): nothing nests. Its store used to be refused inside the claim
    // store, and the mission stayed filled and not completed.
    [Test]
    public void AnEventGiftThatFillsAFieldMission_CompletesItAfterTheClaimStore() {
        const int itemId = 50000103;
        test.EnterMap(TestGameSession.Map(2000023));
        CompletionTables();
        Quest quest = test.StartQuest(TestGameSession.QuestData(990803, QuestType.FieldMission) with {
            Conditions = [new ConditionMetadata(ConditionType.item_exist, 1, new ConditionMetadata.Parameters(Integers: [itemId]), null)],
        });
        ItemMetadata metadata = TestGameSession.Metadata(itemId);
        test.Register(metadata);
        QuestState stateAtCommit = QuestState.None;
        int counterAtCommit = -1;

        List<Item>? given = EventClaim.Give(session, [new Item(metadata, 1, 1)], [], afterCommit: () => {
            stateAtCommit = quest.State;
            counterAtCommit = quest.Conditions[0].Counter;
        });

        Assert.Multiple(() => {
            Assert.That(given, Has.Count.EqualTo(1));
            Assert.That(nested, Is.Empty);
            Assert.That((counterAtCommit, stateAtCommit), Is.EqualTo((1, QuestState.Started)), "filled in the store, completed after it");
            Assert.That((quest.State, quest.CompletionCount), Is.EqualTo((QuestState.Completed, 1)), "completed once its row was stored");
        });
    }

    // Prestige exp gained inside a store (an exp item an event gift brings) that reaches a level: the level and its
    // missions are sent at once, the level's store, item and trophies follow once the store gave its lock back
    // (ExperienceManager.PrestigeLevelUp through AfterStore). The level's store used to be refused there.
    [Test]
    public void APrestigeLevelReachedInsideAStore_IsStoredAfterIt() {
        test.EnterMap(TestGameSession.Map(2000023));
        session.Player.Value.Character.Level = 50;
        long lvUpExp = session.ServerTableMetadata.ConstantsTable.AdventureLevelLvUpExp;
        session.Exp.PrestigeCurrentExp = lvUpExp - 10;
        int level = session.Exp.PrestigeLevel;
        int levelAtCommit = -1;
        bool levelSentAtCommit = false;

        Assert.That(session.TrySavePurchase(db => {
            db.AfterCommit(() => {
                session.Exp.AddPrestigeExp(10);
                levelAtCommit = session.Exp.PrestigeLevel;
                levelSentAtCommit = test.TakeSent().Exists(packet => TestGameSession.Op(packet) == SendOp.Prestige && packet[2] == 2);
            });
            return true;
        }), Is.True);

        Assert.Multiple(() => {
            Assert.That(nested, Is.Empty);
            Assert.That((levelAtCommit, levelSentAtCommit), Is.EqualTo((level + 1, true)), "the level is sent at once");
            Assert.That(session.Exp.PrestigeLevel, Is.EqualTo(level + 1));
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.GetAccount(session.AccountId)!.PrestigeLevel, Is.EqualTo(level + 1), "and stored after the store");
        });
    }

    // The daily reset is taken inside a store (BeginWithResets); its grants start quests, and a field mission that
    // counts the start (quest_accept) completes with a store of its own. The grants run once the store gave its lock
    // back (QuestManager.ResetDailyQuests through AfterStore): nothing nests, and the granted quest is not started
    // inside the store. Its start used to run there, and the mission's completion was refused.
    [Test]
    public void TheDailyResetsGrantInsideAStore_StartsAfterIt() {
        const int fieldQuestId = 91000001;
        test.EnterMap(TestGameSession.Map(2000023));
        CompletionTables();
        Assume.That(session.QuestMetadata.GetQuests(), Is.Empty);
        typeof(QuestMetadataStorage).GetField("groups", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.QuestMetadata, new Dictionary<int, int[]> { [DailyQuestGrant.FieldQuestGroup] = [fieldQuestId] });
        QuestMetadata fieldQuest = TestGameSession.QuestData(fieldQuestId, QuestType.FieldQuest);
        test.Register(fieldQuest with {
            Basic = fieldQuest.Basic with { Repeat = 2 },
            Require = fieldQuest.Require with { Level = 50, GroupId = DailyQuestGrant.FieldQuestGroup },
        });
        session.Player.Value.Character.Level = 50;
        session.Player.Value.Character.FieldQuestGrantTime = new DateTimeOffset(DateTime.Today.AddDays(-1)).ToUnixTimeSeconds();
        Quest mission = test.StartQuest(TestGameSession.QuestData(990804, QuestType.FieldMission) with {
            Conditions = [new ConditionMetadata(ConditionType.quest_accept, 1, new ConditionMetadata.Parameters(Integers: [fieldQuestId]), null)],
        });
        bool? grantedInside = null;

        Assert.That(session.TrySavePurchase(db => {
            session.Quest.ResetDailyQuests(DateTime.Today);
            grantedInside = session.Quest.TryGetQuest(fieldQuestId, out _);
            return true;
        }), Is.True);

        Assert.Multiple(() => {
            Assert.That(nested, Is.Empty);
            Assert.That(grantedInside, Is.False, "granted after the store");
            Assert.That(session.Quest.TryGetQuest(fieldQuestId, out Quest? granted) && granted.State == QuestState.Started);
            Assert.That((mission.State, mission.CompletionCount), Is.EqualTo((QuestState.Completed, 1)));
        });
    }
}

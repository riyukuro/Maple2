using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

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

    [Test]
    public void ASessionSaveInsideAPurchase_IsRefused() {
        bool? inner = null;

        Assert.That(session.TrySavePurchase(_ => {
            inner = session.SessionSave();
            return true;
        }), Is.True);
        Assert.That(inner, Is.False);
        Assert.That(nested, Has.Member("SessionSave"));
    }

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
}

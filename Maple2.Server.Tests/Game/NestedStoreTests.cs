using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
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
}

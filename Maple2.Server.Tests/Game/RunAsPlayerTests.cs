using System.Collections.Generic;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The World's resets and the condition counts its messages bring (a guild or club joined) change what the player's
// field handlers change (the gathering counts, the quests, the claimed rewards). They run as a task of the player's
// field, so never during one of the player's handlers, and not on the World link's thread.
public class RunAsPlayerTests {
    [Test]
    public void InAField_RunsAsATaskOfTheField() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var ran = new List<int>();

        session.RunAsPlayer(() => ran.Add(1));
        Assert.That(ran, Is.Empty);

        test.RunFieldQueue();
        Assert.That(ran, Is.EqualTo(new[] { 1 }));
    }

    // The session went to another field before the task ran: it runs on the field the session plays on now.
    [Test]
    public void MovedBeforeItRan_RunsOnTheNextField() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var first = session.Field!;
        var ran = new List<int>();

        session.RunAsPlayer(() => ran.Add(1));
        test.EnterMap(TestGameSession.Map(2000002));
        first.Scheduler.InvokeAll();
        Assert.That(ran, Is.Empty);

        test.RunFieldQueue();
        Assert.That(ran, Is.EqualTo(new[] { 1 }));
    }

    // Between fields (or a session without one) nothing handles the player's packets: it runs at once.
    [Test]
    public void NoField_RunsAtOnce() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Field = null;
        var ran = new List<int>();

        session.RunAsPlayer(() => ran.Add(1));
        Assert.That(ran, Is.EqualTo(new[] { 1 }));
    }

    // The field the task was queued on runs no queue once the player left it (no players there, FieldManager.Update):
    // the task waits on the session and the next field runs it when the player enters it.
    [Test]
    public void LeftFieldNeverRuns_TheEntryOfTheNextFieldRunsIt() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var ran = new List<int>();

        session.RunAsPlayer(() => ran.Add(1));
        test.EnterMap(TestGameSession.Map(2000002));
        session.RunPlayerActionsAtEntry();
        test.RunFieldQueue();
        Assert.That(ran, Is.EqualTo(new[] { 1 }));
    }

    // A closed session drops what waits (its last save takes the resets).
    [Test]
    public void ClosedSession_DropsThem() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var ran = new List<int>();

        session.RunAsPlayer(() => ran.Add(1));
        typeof(GameSession).GetField("playerActionsClosed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session, true);
        test.RunFieldQueue();
        session.RunPlayerActionsAtEntry();
        test.RunFieldQueue();
        Assert.That(ran, Is.Empty);
    }

    // The tasks run in the order they were asked for, one failing does not stop the next.
    [Test]
    public void InOrder_AFailedOneDoesNotStopTheNext() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var ran = new List<int>();

        session.RunAsPlayer(() => ran.Add(1));
        session.RunAsPlayer(() => throw new System.InvalidOperationException("test"));
        session.RunAsPlayer(() => ran.Add(3));
        test.RunFieldQueue();
        Assert.That(ran, Is.EqualTo(new[] { 1, 3 }));
    }
}

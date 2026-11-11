using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class HomeInviteTrackerTests {
    [Test]
    public void AnswerWithoutAnInviteIsNotPassedOn() {
        var tracker = new HomeInviteTracker();
        Assert.That(tracker.TryAnswer(5, 1000), Is.False);

        tracker.Add(6, 1000);
        Assert.That(tracker.TryAnswer(5, 1000), Is.False);
    }

    [Test]
    public void OneAnswerPerInvite() {
        var tracker = new HomeInviteTracker();
        tracker.Add(5, 1000);

        Assert.That(tracker.TryAnswer(5, 2000), Is.True);
        Assert.That(tracker.TryAnswer(5, 2001), Is.False);
    }

    [Test]
    public void ExpiredInviteIsNotAnswered() {
        var tracker = new HomeInviteTracker();
        tracker.Add(5, 1000);

        Assert.That(tracker.TryAnswer(5, 1000 + HomeInviteTracker.LifetimeMs), Is.False);
        Assert.That(tracker.TryAnswer(5, 1001), Is.False); // the expired try removed it
    }

    [Test]
    public void SecondInviteReplacesTheFirst() {
        var tracker = new HomeInviteTracker();
        tracker.Add(5, 1000);
        tracker.Add(5, 20_000);

        Assert.That(tracker.TryAnswer(5, 1000 + HomeInviteTracker.LifetimeMs + 1), Is.True);
        Assert.That(tracker.TryAnswer(5, 1000 + HomeInviteTracker.LifetimeMs + 2), Is.False);
    }
}

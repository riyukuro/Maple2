using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;

namespace Maple2.Server.Tests.Ingest;

// The game event rows the KR server table marks as unused or test, by their reference note, are dropped at ingest.
public class GameEventScreenTests {
    [TestCase("#1 " + GameEventScreen.UnusedMark + " data")] // a ticket number, then the unused mark (KR 50000 and 50001)
    [TestCase(GameEventScreen.TestMark)] // the test mark alone (KR 660006)
    [TestCase("event shutdown " + GameEventScreen.TestMark)] // the test mark at the end of a note (KR 90000000)
    public void MarkedRows_AreDropped(string reference) {
        Assert.That(GameEventScreen.MarkedNotForPlay(reference), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("MAPLE2-3363 event")]
    [TestCase(GameEventScreen.ContestMark + " event")] // the contest mark holds the test mark but is a real row
    public void OtherRows_AreKept(string? reference) {
        Assert.That(GameEventScreen.MarkedNotForPlay(reference), Is.False);
    }

    // Both KR UserCondition rows are marked unused, so the replay no longer picks the type.
    [Test]
    public void Replay_SkipsUserCondition() {
        Assert.That(AttendGiftReplay.Types, Does.Not.Contain(GameEventType.UserCondition));
        Assert.That(AttendGiftReplay.NeedData, Does.Not.Contain(GameEventType.UserCondition));
    }
}

using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game;

// show_event_result (66200001's team results): the notice goes to the players of the user tag the action names,
// and of its box (or outside it) when it names one.
public class ShowEventResultTests {
    [TestCase(1, 1, true)]
    [TestCase(2, 1, false)]
    [TestCase(1, 2, false)]
    [TestCase(2, 2, true)]
    [TestCase(1, 0, true)]
    [TestCase(2, 0, true)]
    public void UserTag(int playerTagId, int userTagId, bool expected) {
        Assert.That(TriggerContext.ShowsEventResult(playerTagId, null, userTagId, false), Is.EqualTo(expected));
    }

    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    public void Box(bool inBox, bool isOutSide, bool expected) {
        Assert.That(TriggerContext.ShowsEventResult(1, inBox, 0, isOutSide), Is.EqualTo(expected));
    }

    [Test]
    public void TagAndBox_BothMustHold() {
        Assert.That(TriggerContext.ShowsEventResult(1, true, 1, false), Is.True);
        Assert.That(TriggerContext.ShowsEventResult(2, true, 1, false), Is.False);
        Assert.That(TriggerContext.ShowsEventResult(1, false, 1, false), Is.False);
    }
}

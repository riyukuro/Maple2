using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// set_event_ui_script: who a banner goes to. Made-up box ids.
public class TriggerEventUiBoxTests {
    [Test]
    public void NoBox_GoesToTheWholeField() {
        // Most banner actions name no box; box 0 is the whole field.
        (int[] inBoxes, int[] notInBoxes) = TriggerContext.EventUiBoxes([]);
        Assert.That(inBoxes, Is.EqualTo(new[] { 0 }));
        Assert.That(notInBoxes, Is.Empty);

        (inBoxes, notInBoxes) = TriggerContext.EventUiBoxes([""]);
        Assert.That(inBoxes, Is.EqualTo(new[] { 0 }));
        Assert.That(notInBoxes, Is.Empty);

        (inBoxes, notInBoxes) = TriggerContext.EventUiBoxes(["0"]);
        Assert.That(inBoxes, Is.EqualTo(new[] { 0 }));
        Assert.That(notInBoxes, Is.Empty);
    }

    [Test]
    public void NamedBoxes_KeepTheirSide() {
        (int[] inBoxes, int[] notInBoxes) = TriggerContext.EventUiBoxes(["301"]);
        Assert.That(inBoxes, Is.EqualTo(new[] { 301 }));
        Assert.That(notInBoxes, Is.Empty);

        // The losers' banner: everyone outside the winner box, and nobody else.
        (inBoxes, notInBoxes) = TriggerContext.EventUiBoxes(["!301"]);
        Assert.That(inBoxes, Is.Empty);
        Assert.That(notInBoxes, Is.EqualTo(new[] { 301 }));

        (inBoxes, notInBoxes) = TriggerContext.EventUiBoxes(["101", " 102", "!105"]);
        Assert.That(inBoxes, Is.EqualTo(new[] { 101, 102 }));
        Assert.That(notInBoxes, Is.EqualTo(new[] { 105 }));
    }
}

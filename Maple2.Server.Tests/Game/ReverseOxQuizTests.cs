using System;
using System.Text.Json;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Widget;

namespace Maple2.Server.Tests.Game;

// The April Fools reverse OX quiz (KR 581001, 581004, 581007, 581011): while the event runs, the OX quiz's false side
// wins the round.
public class ReverseOxQuizTests {
    // OWins picks the trigger's "Correct" branch (the O box wins) or "Incorrect" (the X box wins).
    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    public void Judge_FlipsWhileReversed(bool isTrue, bool reversed, bool oWins) {
        Assert.That(OxQuizWidget.OWins(isTrue, reversed), Is.EqualTo(oWins));
    }

    // The client has no GameEventReverseOxQuiz class and drops the rest of the event list at an unknown name.
    [Test]
    public void Event_NotSentToClient() {
        var row = new GameEventMetadata(581011, GameEventType.ReverseOxQuiz, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero,
            TimeSpan.Zero, [], new ReverseOxQuiz(), "", "", "", "");
        Assert.That(new GameEvent(row).SentToClient, Is.False);
    }

    // The table is stored as json; the type comes back.
    [Test]
    public void JsonRoundTrip() {
        GameEventData? back = JsonSerializer.Deserialize<GameEventData>(JsonSerializer.Serialize<GameEventData>(new ReverseOxQuiz()));
        Assert.That(back, Is.TypeOf<ReverseOxQuiz>());
    }
}

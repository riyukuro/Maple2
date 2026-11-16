using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// The event OX quiz's widget as its script (61000003) drives it: DevMode, then per round PickQuiz of a level,
// ShowQuiz, ShowAnswer and PreJudge, whose Correct/Incorrect the script reads to drop the losing box. The
// questions are made up; each level holds one so the pick is known.
public class OxQuizWidgetTests {
    private static readonly OxQuizTable.Entry TrueQuestion = new(1, 1, "Nature", "Is the sky blue?", 1, true, "It is.");
    private static readonly OxQuizTable.Entry FalseQuestion = new(2, 1, "Nature", "Is snow black?", 2, false, "It is white.");

    private TestGameSession test = null!;
    private FieldManager field => test.Session.Field!;
    private OxQuizWidget widget = null!;

    private void Begin(bool reversed = false) {
        test = new TestGameSession();
        test.SetServerTable(new OxQuizTable(new Dictionary<int, OxQuizTable.Entry> {
            [TrueQuestion.Id] = TrueQuestion,
            [FalseQuestion.Id] = FalseQuestion,
        }));
        if (reversed) {
            test.AddEvent(581011, GameEventType.ReverseOxQuiz, new ReverseOxQuiz());
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(61000003));
        widget = new OxQuizWidget(field);
        test.TakeSent();
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Round_ShowsTheLevelsQuestionThenItsAnswer() {
        Begin();
        widget.Action("DevMode", 0, "0");
        widget.Action("PickQuiz", 0, "2");

        widget.Action("ShowQuiz", 0, "10");
        widget.Action("ShowAnswer", 0, "5");

        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(QuizEventPacket.Question("Nature", "Is snow black?", "", 10)),
            Hex(QuizEventPacket.Answer(false, "It is white.", 5)),
        }));
    }

    // The script's DevMode 1 puts the answer in the question text; no duration is 15 s.
    [Test]
    public void DevMode_PutsTheAnswerInTheQuestion() {
        Begin();
        widget.Action("DevMode", 0, "1");
        widget.Action("PickQuiz", 0, "1");

        widget.Action("ShowQuiz", 0, "");
        widget.Action("ShowAnswer", 0, "");

        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(QuizEventPacket.Question("Nature", "Is the sky blue?", "\n[DebugMode] TRUE", 15)),
            Hex(QuizEventPacket.Answer(true, "It is.", 15)),
        }));
    }

    [Test]
    public void DevMode_ShowsAFalseAnswerToo() {
        Begin();
        widget.Action("DevMode", 0, "1");
        widget.Action("PickQuiz", 0, "2");

        widget.Action("ShowQuiz", 0, "15");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(QuizEventPacket.Question("Nature", "Is snow black?", "\n[DebugMode] FALSE", 15)) }));
    }

    // A level that is not a number picks from level 1.
    [Test]
    public void PickQuiz_LevelNotANumber_PicksLevelOne() {
        Begin();
        widget.Action("PickQuiz", 0, "x");

        widget.Action("ShowQuiz", 0, "15");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(QuizEventPacket.Question("Nature", "Is the sky blue?", "", 15)) }));
    }

    // A level with no question leaves the round empty: nothing is shown or judged.
    [Test]
    public void EmptyRound_ShowsAndJudgesNothing() {
        Begin();
        widget.Action("PickQuiz", 0, "9");

        widget.Action("ShowQuiz", 0, "15");
        widget.Action("ShowAnswer", 0, "15");
        widget.Action("PreJudge", 0, "");

        Assert.That(Sent(), Is.Empty);
        Assert.That(widget.Check("Correct", ""), Is.False);
        Assert.That(widget.Check("Incorrect", ""), Is.False);
    }

    [Test]
    public void PreJudge_TrueQuestion_OWins() {
        Begin();
        widget.Action("PickQuiz", 0, "1");

        widget.Action("PreJudge", 0, "");

        Assert.That(widget.Check("Correct", ""), Is.True);
        Assert.That(widget.Check("Incorrect", ""), Is.False);
    }

    [Test]
    public void PreJudge_FalseQuestion_XWins() {
        Begin();
        widget.Action("PickQuiz", 0, "2");

        widget.Action("PreJudge", 0, "");

        Assert.That(widget.Check("Correct", ""), Is.False);
        Assert.That(widget.Check("Incorrect", ""), Is.True);
    }

    // A running ReverseOxQuiz event flips the winning side; the shown answer stays the true one.
    [Test]
    public void PreJudge_Reversed_FlipsTheWinner() {
        Begin(reversed: true);
        widget.Action("PickQuiz", 0, "1");

        widget.Action("PreJudge", 0, "");
        widget.Action("ShowAnswer", 0, "15");

        Assert.That(widget.Check("Correct", ""), Is.False);
        Assert.That(widget.Check("Incorrect", ""), Is.True);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(QuizEventPacket.Answer(true, "It is.", 15)) }));
    }

    // The next round's PreJudge reads its own question.
    [Test]
    public void SecondRound_JudgesItsOwnQuestion() {
        Begin();
        widget.Action("PickQuiz", 0, "1");
        widget.Action("PreJudge", 0, "");
        widget.Action("PickQuiz", 0, "2");

        widget.Action("PreJudge", 0, "");

        Assert.That(widget.Check("Incorrect", ""), Is.True);
    }

    // Judge and Winner are the script's own steps; an unknown function is logged and ignored.
    [Test]
    public void OtherFunctions_DoNothing() {
        Begin();
        widget.Action("PickQuiz", 0, "1");

        widget.Action("Judge", 0, "");
        widget.Action("Winner", 0, "");
        widget.Action("NoSuchFunction", 0, "");

        Assert.That(Sent(), Is.Empty);
        Assert.That(widget.Check("Correct", ""), Is.False);
    }
}

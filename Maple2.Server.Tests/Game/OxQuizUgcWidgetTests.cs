using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The OX quiz stadium's widget as the stadium script (map 61000011, trigger "main") drives it: the checks it reads
// the host's moves through, and the actions that show the host's question and answer, move the rounds on and end the
// game. The host and entrants other than the session are made-up ids.
public class OxQuizUgcWidgetTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long OtherHost = 990001;
    private const long Entrant = 990002;
    private const long Fee = 2000;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private FieldManager field => session.Field!;
    private OxQuizUgcWidget widget = null!;
    private UgcOxEvent ox = null!;

    // The session is the host unless hostCharacterId names another one.
    private void Begin(long? hostCharacterId = null, int share = 50, bool withEvent = true) {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(UgcOxRules.StadiumMapId));
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        typeof(FieldManager.Factory).GetField("ugcOxEvents", Any)!.SetValue(factory, new ConcurrentDictionary<long, UgcOxEvent>());
        typeof(FieldManager.Factory).GetField("logger", Any)!.SetValue(factory, Serilog.Log.Logger);
        typeof(FieldManager).GetField("<FieldFactory>k__BackingField", Any)!.SetValue(field, factory);
        if (field.TriggerObjects == null) {
            var objects = (TriggerCollection) RuntimeHelpers.GetUninitializedObject(typeof(TriggerCollection));
            typeof(TriggerCollection).GetField(nameof(TriggerCollection.Boxes), Any)!.SetValue(objects, new Dictionary<int, TriggerBox>());
            typeof(FieldManager).GetField(nameof(FieldManager.TriggerObjects), Any)!.SetValue(field, objects);
        }
        long host = hostCharacterId ?? session.CharacterId;
        ox = new UgcOxEvent(session.AccountId, host, "Host", 0, new UgcOxRules.Settings(Fee, 20, share));
        if (withEvent) {
            field.SetUgcOx(ox);
        }
        widget = new OxQuizUgcWidget(field);
        test.TakeSent();
    }

    private void Run() {
        ox.Enter(Entrant, _ => true);
        ox.Enter(Entrant + 1, _ => true);
        Assert.That(ox.Start(), Is.True);
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // The script waits on IsStarted, then each round on IsQuizSubmit and IsAnswerSubmit and reads the answer.
    [Test]
    public void Checks_FollowTheHostsMoves() {
        Begin();
        Assert.That(widget.Check("IsStarted", ""), Is.False);
        Run();
        widget.Action("StartRound", 0, "");
        widget.Action("HostUIChange", 0, "InputQuiz");

        Assert.Multiple(() => {
            Assert.That(widget.Check("IsStarted", ""), Is.True);
            Assert.That(widget.Check("IsQuizSubmit", ""), Is.False);
        });
        Assert.That(ox.SubmitQuestion("Is the sky blue?"), Is.True);
        widget.Action("HostUIChange", 0, "SelectAnswer");
        Assert.That(ox.SubmitAnswer(true), Is.True);

        Assert.Multiple(() => {
            Assert.That(widget.Check("IsQuizSubmit", ""), Is.True);
            Assert.That(widget.Check("IsAnswerSubmit", ""), Is.True);
            Assert.That(widget.Check("Correct", ""), Is.True);
            Assert.That(widget.Check("Incorrect", ""), Is.False);
            Assert.That(widget.Check("IsCanceled", ""), Is.False);
            Assert.That(widget.Check("IsFinished", ""), Is.False);
            Assert.That(widget.Check("IsRemoveWall", ""), Is.False);
            Assert.That(widget.Check("NoSuchCheck", ""), Is.False);
        });
    }

    [Test]
    public void Checks_Canceled_WhenTheStartHadTooFewEntrants() {
        Begin();
        ox.Enter(Entrant, _ => true);

        Assert.That(ox.Start(), Is.False);

        Assert.That(widget.Check("IsCanceled", ""), Is.True);
        Assert.That(widget.Check("IsStarted", ""), Is.False);
    }

    // A stadium without an event answers false and does nothing.
    [Test]
    public void WithoutAnEvent_ChecksAreFalseAndActionsDoNothing() {
        Begin(withEvent: false);

        widget.Action("ShowHostUI", 0, "");
        widget.Action("StartRound", 0, "");

        Assert.That(widget.Check("IsStarted", ""), Is.False);
        Assert.That(Sent(), Is.Empty);
        Assert.That(ox.Round, Is.EqualTo(0));
    }

    [Test]
    public void ShowHostUI_AtTheWelcome_ShowsTheWelcomeStage() {
        Begin();
        ox.Enter(Entrant, _ => true);

        widget.Action("ShowHostUI", 0, "");

        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(HomeActionPacket.UgcOxStage(session.AccountId, HomeActionPacket.UgcOxStageStep.Welcome, 0, Fee, Fee / 2)),
        }));
    }

    [Test]
    public void ShowHostUI_OnceStarted_ShowsNothing() {
        Begin();
        Run();

        widget.Action("ShowHostUI", 0, "");

        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void StartRound_NextRoundWithTheRoundStage() {
        Begin();
        Run();

        widget.Action("StartRound", 0, "");

        Assert.That(ox.Round, Is.EqualTo(1));
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(HomeActionPacket.UgcOxStage(session.AccountId, HomeActionPacket.UgcOxStageStep.Round, 1, 2 * Fee, Fee)),
        }));
    }

    // InputQuiz and SelectAnswer open the host's dialog; Move and Judge only set the step.
    [TestCase("InputQuiz", true)]
    [TestCase("SelectAnswer", true)]
    [TestCase("Move", false)]
    [TestCase("Judge", false)]
    public void HostUIChange_SetsTheStep_AndOpensTheHostsDialog(string step, bool dialog) {
        Begin();
        Run();
        widget.Action("StartRound", 0, "");
        test.TakeSent();

        widget.Action("HostUIChange", 0, step);

        Assert.That(ox.HostStep, Is.EqualTo(Enum.Parse<UgcOxHostStep>(step)));
        string[] sent = Sent();
        if (dialog) {
            var mode = Enum.Parse<HomeActionPacket.UgcOxHostMode>(step);
            Assert.That(sent, Is.EqualTo(new[] { Hex(HomeActionPacket.UgcOxHostDialog(mode, 1)) }));
        } else {
            Assert.That(sent, Is.Empty);
        }
    }

    // The dialog goes to the host only: a stadium player who is not the host gets nothing.
    [Test]
    public void HostUIChange_HostNotInTheStadium_SendsNothing() {
        Begin(hostCharacterId: OtherHost);
        Run();

        widget.Action("HostUIChange", 0, "InputQuiz");

        Assert.That(ox.HostStep, Is.EqualTo(UgcOxHostStep.InputQuiz));
        Assert.That(Sent(), Is.Empty);
    }

    [TestCase("None")]
    [TestCase("Unknown")]
    public void HostUIChange_UnknownStep_IsIgnored(string step) {
        Begin();
        Run();
        widget.Action("HostUIChange", 0, "InputQuiz");
        test.TakeSent();

        widget.Action("HostUIChange", 0, step);

        Assert.That(ox.HostStep, Is.EqualTo(UgcOxHostStep.InputQuiz));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void ShowQuiz_ShowsTheHostsQuestion_AndSpendsTheWallRemoval() {
        Begin();
        Run();
        widget.Action("StartRound", 0, "");
        widget.Action("HostUIChange", 0, "InputQuiz");
        ox.SubmitQuestion("Is the sky blue?");
        widget.Action("HostUIChange", 0, "SelectAnswer");
        Assert.That(ox.RemoveWall(), Is.True);
        Assert.That(widget.Check("IsRemoveWall", ""), Is.True);
        test.TakeSent();

        widget.Action("ShowQuiz", 0, "20");

        Assert.That(widget.Check("IsRemoveWall", ""), Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(QuizEventPacket.Question(string.Empty, ox.Question!, string.Empty, 20)) }));
    }

    // Without a question the quiz shows nothing; a duration that is no number is 15 s.
    [Test]
    public void ShowQuiz_NoQuestion_ShowsNothing() {
        Begin();
        Run();
        widget.Action("StartRound", 0, "");
        test.TakeSent();

        widget.Action("ShowQuiz", 0, "");

        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void ShowAnswer_ShowsTheHostsAnswer() {
        Begin();
        Run();
        widget.Action("StartRound", 0, "");
        widget.Action("HostUIChange", 0, "SelectAnswer");
        ox.SubmitAnswer(false);
        test.TakeSent();

        widget.Action("ShowAnswer", 0, "x");

        Assert.That(widget.Check("Incorrect", ""), Is.True);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(QuizEventPacket.Answer(false, string.Empty, 15)) }));
    }

    [Test]
    public void ShowAnswer_NoAnswer_ShowsNothing() {
        Begin();
        Run();
        widget.Action("StartRound", 0, "");
        test.TakeSent();

        widget.Action("ShowAnswer", 0, "10");

        Assert.That(Sent(), Is.Empty);
    }

    // The last round's end makes the game finished; an earlier one only clears the host's step.
    [Test]
    public void EndRound_TheLastRoundFinishesTheGame() {
        Begin();
        Run();
        for (int round = 1; round < UgcOxRules.MaxRounds; round++) {
            widget.Action("StartRound", 0, "");
            widget.Action("HostUIChange", 0, "Judge");
            widget.Action("EndRound", 0, "");
            Assert.That(widget.Check("IsFinished", ""), Is.False);
        }
        Assert.That(ox.HostStep, Is.EqualTo(UgcOxHostStep.None));

        widget.Action("StartRound", 0, "");
        widget.Action("EndRound", 0, "");

        Assert.That(widget.Check("IsFinished", ""), Is.True);
    }

    // The game's end shows the close stage and pays out once: with no one on the stage box the host, in the
    // stadium, gets the whole pot.
    [Test]
    public void EndGame_NoWinner_PaysTheHostThePotOnce() {
        Begin();
        Run();
        long before = session.Currency.Meso;

        widget.Action("EndGame", 0, "");
        widget.Action("EndGame", 0, "");

        Assert.Multiple(() => {
            Assert.That(ox.Settled, Is.True);
            Assert.That(ox.Phase, Is.EqualTo(UgcOxPhase.Ended));
            Assert.That(session.Currency.Meso, Is.EqualTo(before + 2 * Fee));
            Assert.That(test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.HomeAction), Is.EqualTo(1));
        });
    }

    // An entrant standing on the stage box at the end is a winner and gets its part of the pot.
    [Test]
    public void EndGame_AnEntrantOnTheStageBox_WinsThePot() {
        Begin(hostCharacterId: OtherHost, share: 100);
        ((Dictionary<int, TriggerBox>) field.TriggerObjects.Boxes)[OxQuizUgcWidget.StageBoxId] =
            new TriggerBox(new Ms2TriggerBox(Vector3.Zero, new Vector3(300, 300, 300), OxQuizUgcWidget.StageBoxId, true));
        session.Player.Position = Vector3.Zero;
        ox.Enter(session.CharacterId, _ => true);
        ox.Enter(Entrant, _ => true);
        Assert.That(ox.Start(), Is.True);
        long before = session.Currency.Meso;

        widget.Action("EndGame", 0, "");

        Assert.That(session.Currency.Meso, Is.EqualTo(before + 2 * Fee));
    }

    // The game ends canceled: every entrant's fee goes back (the session, an entrant in the stadium, gets its own).
    [Test]
    public void EndGame_Canceled_RefundsTheFees() {
        Begin(hostCharacterId: OtherHost);
        ox.Enter(session.CharacterId, _ => true);
        Assert.That(ox.Cancel(), Is.True);
        long before = session.Currency.Meso;

        widget.Action("EndGame", 0, "");

        Assert.That(session.Currency.Meso, Is.EqualTo(before + Fee));
    }

    // CreateWall and PreJudge are the script's own; an unknown function is only logged.
    [TestCase("CreateWall")]
    [TestCase("PreJudge")]
    [TestCase("NoSuchFunction")]
    public void ScriptOnlyFunctions_DoNothing(string function) {
        Begin();
        Run();

        widget.Action(function, 0, "");

        Assert.That(Sent(), Is.Empty);
        Assert.That(ox.Round, Is.EqualTo(0));
    }
}

using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The typing-game dungeons (02000533_bf, 02000535_bf, 02000555_bf): TypingGame Start, the client's 1/0 reply,
// and the Round widget that the gamelogic_9002 scripts step through.
public class TypingGameRoundsTests {
    private static RoundCounter ScriptRounds() {
        var rounds = new RoundCounter();
        rounds.Action("SettingFinalRound", 1);
        rounds.Action("SettingAllowedFailCount", 3);
        rounds.Action("SettingRoundInitIfFail", 0);
        return rounds;
    }

    // The script's check state: GameClear and GameFail first, then which "CurrentRound = N" state runs.
    private static string Next(RoundCounter rounds) {
        if (WidgetCondition.Matches(rounds.Value("GameClear"), "")) return "clear";
        if (WidgetCondition.Matches(rounds.Value("GameFail"), "")) return "fail";
        foreach (int n in new[] { 1, 2, 3 }) {
            if (WidgetCondition.Matches(rounds.Value("CurrentRound"), $"= {n}")) return $"round{n}";
        }
        return "stall";
    }

    [TestCase(new[] { 1 }, new[] { "round1", "clear" })]
    [TestCase(new[] { 0, 1 }, new[] { "round1", "round2", "clear" })]
    [TestCase(new[] { 0, 0, 1 }, new[] { "round1", "round2", "round3", "clear" })]
    [TestCase(new[] { 0, 0, 0 }, new[] { "round1", "round2", "round3", "fail" })]
    public void ScriptFlow(int[] results, string[] states) {
        RoundCounter rounds = ScriptRounds();
        var seen = new System.Collections.Generic.List<string> { Next(rounds) };
        foreach (int result in results) {
            rounds.Action("RoundResult", result);
            seen.Add(Next(rounds));
        }
        Assert.That(seen, Is.EqualTo(states));
    }

    [Test]
    public void ResultsAfterTheEndChangeNothing() {
        RoundCounter rounds = ScriptRounds();
        rounds.Action("RoundResult", 1);
        rounds.Action("RoundResult", 0);
        Assert.Multiple(() => {
            Assert.That(Next(rounds), Is.EqualTo("clear"));
            Assert.That(rounds.Fails, Is.Zero);
        });
    }

    [Test]
    public void InitWidgetRoundStartsOverWithTheSettings() {
        RoundCounter rounds = ScriptRounds();
        rounds.Action("RoundResult", 0);
        rounds.Action("RoundResult", 0);
        rounds.Action("RoundResult", 0);
        rounds.Action("InitWidgetRound", 0);
        Assert.Multiple(() => {
            Assert.That(Next(rounds), Is.EqualTo("round1"));
            Assert.That(rounds.AllowedFailCount, Is.EqualTo(3));
            Assert.That(rounds.FinalRound, Is.EqualTo(1));
        });
    }

    [Test]
    public void InitIfFailGoesBackToRoundOne() {
        var rounds = new RoundCounter();
        rounds.Action("SettingFinalRound", 3);
        rounds.Action("SettingAllowedFailCount", 2);
        rounds.Action("SettingRoundInitIfFail", 1);
        rounds.Action("RoundResult", 1);
        rounds.Action("RoundResult", 1);
        Assert.That(rounds.CurrentRound, Is.EqualTo(3));
        rounds.Action("RoundResult", 0);
        Assert.Multiple(() => {
            Assert.That(rounds.CurrentRound, Is.EqualTo(1));
            Assert.That(rounds.Wins, Is.Zero);
            Assert.That(Next(rounds), Is.EqualTo("round1"));
        });
    }

    [TestCase(1, "= 1", true)]
    [TestCase(2, "= 1", false)]
    [TestCase(2, "=2", true)]
    [TestCase(2, "== 2", true)]
    [TestCase(3, ">= 2", true)]
    [TestCase(1, "> 1", false)]
    [TestCase(1, "< 2", true)]
    [TestCase(2, "<= 1", false)]
    [TestCase(2, "!= 1", true)]
    [TestCase(0, "0", true)]
    [TestCase(1, "0", false)]
    [TestCase(1, "", true)]
    [TestCase(0, "", false)]
    [TestCase(1, "= x", false)]
    public void Conditions(int value, string condition, bool expected) {
        Assert.That(WidgetCondition.Matches(value, condition), Is.EqualTo(expected));
    }

    // A game with no reply yet matches neither Result "1" nor "0".
    [Test]
    public void UnsetValueMatchesNothing() {
        Assert.Multiple(() => {
            Assert.That(WidgetCondition.Matches(null, "0"), Is.False);
            Assert.That(WidgetCondition.Matches(null, "1"), Is.False);
            Assert.That(WidgetCondition.Matches(null, ""), Is.False);
        });
    }

    [Test]
    public void FirstReplyToAStartedGameCounts() {
        var game = new TypingGameState();
        Assert.Multiple(() => {
            Assert.That(game.Reply(1), Is.False);
            Assert.That(game.Result, Is.Null);
        });
        game.Start();
        Assert.That(game.Result, Is.Null);
        Assert.Multiple(() => {
            Assert.That(game.Reply(0), Is.True);
            Assert.That(game.Reply(1), Is.False);
            Assert.That(game.Result, Is.EqualTo(0));
        });
        game.Start();
        Assert.Multiple(() => {
            Assert.That(game.Result, Is.Null);
            Assert.That(game.Reply(1), Is.True);
            Assert.That(game.Result, Is.EqualTo(1));
        });
    }

    // CTriggerService::OnTypingGame reads int count then int limit (ms) after Ui sub-command 0xc.
    [Test]
    public void TypingGamePacket() {
        ByteWriter writer = TriggerPacket.UiTypingGame(7, 6000);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Trigger));
            Assert.That(reader.ReadByte(), Is.EqualTo(9));
            Assert.That(reader.ReadByte(), Is.EqualTo(0xc));
            Assert.That(reader.ReadInt(), Is.EqualTo(7));
            Assert.That(reader.ReadInt(), Is.EqualTo(6000));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4 + 4));
        });
    }
}

using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The home OX quiz event's state as the host's commands and the stadium script move it. Made-up ids.
public class UgcOxEventTests {
    private const long HostAccount = 100;
    private const long Host = 1;

    private static UgcOxEvent Make(int max = 10) {
        return new UgcOxEvent(HostAccount, Host, "Host", 62000001, new UgcOxRules.Settings(1000, max, 50));
    }

    private static UgcOxEvent Running(int rounds = 0) {
        UgcOxEvent ox = Make();
        ox.Enter(2);
        ox.Enter(3);
        Assert.That(ox.Start(), Is.True);
        for (int i = 0; i < rounds; i++) {
            ox.NextRound();
        }
        return ox;
    }

    [Test]
    public void EnterUntilFull() {
        UgcOxEvent ox = Make(max: 2);
        Assert.Multiple(() => {
            Assert.That(ox.Enter(Host), Is.EqualTo(UgcOxEnter.Host));
            Assert.That(ox.Enter(2), Is.EqualTo(UgcOxEnter.New));
            Assert.That(ox.Enter(2), Is.EqualTo(UgcOxEnter.Rejoin));
            Assert.That(ox.Enter(3), Is.EqualTo(UgcOxEnter.New));
            Assert.That(ox.Enter(4), Is.EqualTo(UgcOxEnter.Full));
            Assert.That(ox.EntrantCount, Is.EqualTo(2));
            Assert.That(ox.IsEntrant(Host), Is.False);
        });
    }

    [Test]
    public void NoEntryAfterStart() {
        UgcOxEvent ox = Running();
        Assert.Multiple(() => {
            Assert.That(ox.Enter(4), Is.EqualTo(UgcOxEnter.Closed));
            Assert.That(ox.Enter(2), Is.EqualTo(UgcOxEnter.Closed));
            Assert.That(ox.Enter(Host), Is.EqualTo(UgcOxEnter.Host));
        });
        ox.End();
        Assert.That(ox.Enter(Host), Is.EqualTo(UgcOxEnter.Closed));
    }

    [Test]
    public void StartWithTooFewCancels() {
        UgcOxEvent ox = Make();
        ox.Enter(2);
        Assert.Multiple(() => {
            Assert.That(ox.Start(), Is.False);
            Assert.That(ox.Canceled, Is.True);
            Assert.That(ox.Open, Is.False);
            Assert.That(ox.Phase, Is.EqualTo(UgcOxPhase.Welcome));
        });
    }

    [Test]
    public void FinishByPhaseAndRound() {
        UgcOxEvent welcome = Make();
        Assert.That(welcome.Finish(), Is.EqualTo(UgcOxFinish.Ok));
        Assert.That(welcome.Canceled, Is.True);

        UgcOxEvent early = Running(rounds: 1);
        Assert.That(early.Finish(), Is.EqualTo(UgcOxFinish.TooEarly));
        Assert.That(early.Finished, Is.False);

        UgcOxEvent late = Running(rounds: 2);
        Assert.That(late.Finish(), Is.EqualTo(UgcOxFinish.Ok));
        Assert.That(late.Finished, Is.True);
        Assert.That(late.Canceled, Is.False);
        Assert.That(late.Cancel(), Is.False);
    }

    [Test]
    public void LastRoundFinishes() {
        UgcOxEvent ox = Running(rounds: UgcOxRules.MaxRounds - 1);
        ox.EndRound();
        Assert.That(ox.Finished, Is.False);
        ox.NextRound();
        ox.EndRound();
        Assert.That(ox.Finished, Is.True);
    }

    [Test]
    public void QuestionAndAnswerOnlyAtTheirStep() {
        UgcOxEvent ox = Running(rounds: 1);
        Assert.That(ox.SubmitQuestion("Q?"), Is.False);
        ox.SetHostStep(UgcOxHostStep.InputQuiz);
        Assert.Multiple(() => {
            Assert.That(ox.SubmitQuestion("  "), Is.False);
            Assert.That(ox.SubmitQuestion("Q?"), Is.True);
            Assert.That(ox.SubmitQuestion("Again?"), Is.False);
            Assert.That(ox.Question, Is.EqualTo("Q?"));
            Assert.That(ox.SubmitAnswer(true), Is.False);
        });
        ox.SetHostStep(UgcOxHostStep.SelectAnswer);
        Assert.Multiple(() => {
            Assert.That(ox.RemoveWall(), Is.True);
            Assert.That(ox.WallRemoved, Is.True);
            Assert.That(ox.SubmitAnswer(false), Is.True);
            Assert.That(ox.SubmitAnswer(true), Is.False);
            Assert.That(ox.Answer, Is.False);
            Assert.That(ox.RemoveWall(), Is.False);
        });
        ox.NextRound();
        Assert.Multiple(() => {
            Assert.That(ox.Question, Is.Null);
            Assert.That(ox.Answer, Is.Null);
            Assert.That(ox.WallRemoved, Is.False);
        });
    }

    [Test]
    public void EndsOnce() {
        UgcOxEvent ox = Running();
        Assert.That(ox.End(), Is.True);
        Assert.That(ox.End(), Is.False);
        Assert.That(ox.Open, Is.False);
    }

    [Test]
    public void PromoteLimitsAndCooldown() {
        UgcOxEvent ox = Make();
        Assert.Multiple(() => {
            Assert.That(ox.PromoteCooldownLeft(0), Is.Zero);
            Assert.That(ox.Promote(1000), Is.EqualTo(UgcOxPromote.Ok));
            Assert.That(ox.PromoteCooldownLeft(11000), Is.EqualTo(UgcOxRules.PromoteCooldownMs - 10000));
            Assert.That(ox.Promote(30000), Is.EqualTo(UgcOxPromote.Cooldown));
            Assert.That(ox.Promote(61000), Is.EqualTo(UgcOxPromote.Ok));
            Assert.That(ox.Promote(121000), Is.EqualTo(UgcOxPromote.Ok));
            Assert.That(ox.Promote(999999), Is.EqualTo(UgcOxPromote.Spent));
            Assert.That(ox.PromoteCount, Is.EqualTo(UgcOxRules.PromoteTotal));
        });
    }

    [Test]
    public void MoveBoxLayout() {
        ByteWriter writer = HomeActionPacket.UgcOxMove(62000001, 7, HostAccount, "Host", 1000, 10, 50);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.HomeAction));
            Assert.That(reader.ReadByte(), Is.EqualTo(10));
            Assert.That(reader.ReadByte(), Is.EqualTo(5));
            Assert.That(reader.ReadInt(), Is.EqualTo(62000001));
            Assert.That(reader.ReadInt(), Is.EqualTo(7));
            Assert.That(reader.ReadLong(), Is.EqualTo(HostAccount));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Host"));
            Assert.That(reader.ReadLong(), Is.EqualTo(1000));
            Assert.That(reader.ReadInt(), Is.EqualTo(10));
            Assert.That(reader.ReadInt(), Is.EqualTo(50));
            Assert.That(writer.Length, Is.EqualTo(2 + 2 + 4 + 4 + 8 + 2 + 8 + 8 + 4 + 4));
        });
    }

    [Test]
    public void StageLayout() {
        ByteWriter writer = HomeActionPacket.UgcOxStage(HostAccount, HomeActionPacket.UgcOxStageStep.Round, 3, 0, 0);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.HomeAction));
            Assert.That(reader.ReadByte(), Is.EqualTo(10));
            Assert.That(reader.ReadByte(), Is.EqualTo(13));
            Assert.That(reader.ReadLong(), Is.EqualTo(HostAccount));
            Assert.That(reader.ReadByte(), Is.EqualTo(11));
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(writer.Length, Is.EqualTo(2 + 2 + 8 + 2 + 8 + 8));
        });
    }
}

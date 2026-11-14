using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    // A fee taker that always takes the fee.
    private static bool Paid(long fee) => true;

    private static UgcOxEvent Make(int max = 10) {
        return new UgcOxEvent(HostAccount, Host, "Host", 62000001, new UgcOxRules.Settings(1000, max, 50));
    }

    private static UgcOxEvent Running(int rounds = 0) {
        UgcOxEvent ox = Make();
        ox.Enter(2, Paid);
        ox.Enter(3, Paid);
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
            Assert.That(ox.Enter(Host, Paid), Is.EqualTo(UgcOxEnter.Host));
            Assert.That(ox.Enter(2, Paid), Is.EqualTo(UgcOxEnter.New));
            Assert.That(ox.Enter(2, Paid), Is.EqualTo(UgcOxEnter.Rejoin));
            Assert.That(ox.Enter(3, Paid), Is.EqualTo(UgcOxEnter.New));
            Assert.That(ox.Enter(4, Paid), Is.EqualTo(UgcOxEnter.Full));
            Assert.That(ox.EntrantCount, Is.EqualTo(2));
            Assert.That(ox.IsEntrant(Host), Is.False);
        });
    }

    [Test]
    public void NoEntryAfterStart() {
        UgcOxEvent ox = Running();
        Assert.Multiple(() => {
            Assert.That(ox.Enter(4, Paid), Is.EqualTo(UgcOxEnter.Closed));
            Assert.That(ox.Enter(2, Paid), Is.EqualTo(UgcOxEnter.Closed));
            Assert.That(ox.Enter(Host, Paid), Is.EqualTo(UgcOxEnter.Host));
        });
        ox.Settle([], refundAll: false);
        Assert.That(ox.Enter(Host, Paid), Is.EqualTo(UgcOxEnter.Closed));
    }

    [Test]
    public void StartWithTooFewCancels() {
        UgcOxEvent ox = Make();
        ox.Enter(2, Paid);
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
        Assert.That(ox.Settle([2], refundAll: false), Is.Not.Null);
        Assert.That(ox.Settle([2], refundAll: false), Is.Null);
        Assert.That(ox.Settled, Is.True);
        Assert.That(ox.Open, Is.False);
    }

    [Test]
    public void EntryWithoutTheFeeIsRefused() {
        UgcOxEvent ox = Make();
        long asked = 0;
        Assert.Multiple(() => {
            Assert.That(ox.Enter(2, fee => { asked = fee; return false; }), Is.EqualTo(UgcOxEnter.NoFee));
            Assert.That(asked, Is.EqualTo(1000));
            Assert.That(ox.IsEntrant(2), Is.False);
            Assert.That(ox.Pot, Is.Zero);
        });
    }

    [Test]
    public void OnlyANewEntrantPays() {
        UgcOxEvent ox = Make();
        int taken = 0;
        bool Take(long fee) {
            taken++;
            return true;
        }
        ox.Enter(Host, Take);
        ox.Enter(2, Take);
        ox.Enter(2, Take);
        ox.Enter(3, Take);
        Assert.Multiple(() => {
            Assert.That(taken, Is.EqualTo(2));
            Assert.That(ox.Pot, Is.EqualTo(2000));
            Assert.That(ox.Prizes(), Is.EqualTo((2000L, 1000L)));
        });
    }

    [Test]
    public void PayoutSplitsThePot() {
        // 3 entrants at 1000, share 50: 1500 for 2 winners is 750 each, the host keeps 1500.
        UgcOxEvent ox = Make();
        ox.Enter(2, Paid);
        ox.Enter(3, Paid);
        ox.Enter(4, Paid);
        ox.Start();
        IReadOnlyList<UgcOxPayment>? payments = ox.Settle([2, 4, Host, 99], refundAll: false);
        Assert.That(payments, Is.EqualTo(new[] {
            new UgcOxPayment(2, 750, UgcOxPaymentKind.Prize),
            new UgcOxPayment(4, 750, UgcOxPaymentKind.Prize),
            new UgcOxPayment(Host, 1500, UgcOxPaymentKind.Commission),
        }));
    }

    [Test]
    public void PayoutWithOddPotRoundsDownForTheWinners() {
        // Each winner's part is rounded down; the rest, the rounding included, is the host's.
        Assert.Multiple(() => {
            Assert.That(UgcOxRules.Payout(3000, 100, 2), Is.EqualTo(new UgcOxRules.Split(1500, 0)));
            Assert.That(UgcOxRules.Payout(1000, 100, 3), Is.EqualTo(new UgcOxRules.Split(333, 1)));
            Assert.That(UgcOxRules.Payout(7000, 30, 4), Is.EqualTo(new UgcOxRules.Split(525, 4900)));
            Assert.That(UgcOxRules.Payout(9000, 10, 7), Is.EqualTo(new UgcOxRules.Split(128, 8104)));
            Assert.That(UgcOxRules.Payout(1000, 50, 0), Is.EqualTo(new UgcOxRules.Split(0, 1000)));
            Assert.That(UgcOxRules.Payout(0, 50, 2), Is.EqualTo(new UgcOxRules.Split(0, 0)));
        });
        // Whatever the split, nothing is made or lost.
        for (long pot = 0; pot <= 40 * 9000; pot += 1000) {
            foreach (int share in UgcOxRules.Shares) {
                for (int winners = 0; winners <= 40; winners++) {
                    UgcOxRules.Split split = UgcOxRules.Payout(pot, share, winners);
                    Assert.That(split.EachWinner * winners + split.Host, Is.EqualTo(pot));
                    Assert.That(split.Host, Is.GreaterThanOrEqualTo(pot * (100 - share) / 100));
                }
            }
        }
    }

    [Test]
    public void PayoutWithNoWinnerGoesToTheHost() {
        UgcOxEvent ox = Running();
        Assert.That(ox.Settle([], refundAll: false), Is.EqualTo(new[] {
            new UgcOxPayment(Host, 2000, UgcOxPaymentKind.Commission),
        }));
    }

    [Test]
    public void CancelRefundsEveryEntrant() {
        UgcOxEvent ox = Make();
        ox.Enter(2, Paid);
        ox.Start();
        Assert.That(ox.Canceled, Is.True);
        Assert.That(ox.Settle([2], refundAll: false), Is.EqualTo(new[] {
            new UgcOxPayment(2, 1000, UgcOxPaymentKind.Refund),
        }));

        // The host's leave at the welcome, with an entrant who left the stadium since: both get the fee back.
        UgcOxEvent left = Make();
        left.Enter(2, Paid);
        left.Enter(3, Paid);
        left.Cancel();
        Assert.That(left.Settle([], refundAll: false), Is.EqualTo(new[] {
            new UgcOxPayment(2, 1000, UgcOxPaymentKind.Refund),
            new UgcOxPayment(3, 1000, UgcOxPaymentKind.Refund),
        }));
    }

    [Test]
    public void HostLeavingMidGameRefunds() {
        UgcOxEvent ox = Running(rounds: 3);
        Assert.That(ox.Cancel(), Is.True);
        Assert.That(ox.Settle([2], refundAll: false)!.Select(payment => payment.Kind), Is.All.EqualTo(UgcOxPaymentKind.Refund));
    }

    [Test]
    public void ShutdownRefundsAGameThatRan() {
        UgcOxEvent ox = Running(rounds: 3);
        Assert.That(ox.Settle([2], refundAll: true), Is.EqualTo(new[] {
            new UgcOxPayment(2, 1000, UgcOxPaymentKind.Refund),
            new UgcOxPayment(3, 1000, UgcOxPaymentKind.Refund),
        }));
    }

    [Test]
    public void SettlesOnce() {
        UgcOxEvent ox = Running();
        IReadOnlyList<UgcOxPayment>? first = null;
        int settled = 0;
        Parallel.For(0, 16, _ => {
            if (ox.Settle([2], refundAll: false) is { } payments) {
                Interlocked.Increment(ref settled);
                first = payments;
            }
        });
        Assert.Multiple(() => {
            Assert.That(settled, Is.EqualTo(1));
            Assert.That(first!.Sum(payment => payment.Amount), Is.EqualTo(2000));
            Assert.That(ox.Settle([], refundAll: true), Is.Null);
            Assert.That(ox.Enter(4, Paid), Is.EqualTo(UgcOxEnter.Closed));
        });
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

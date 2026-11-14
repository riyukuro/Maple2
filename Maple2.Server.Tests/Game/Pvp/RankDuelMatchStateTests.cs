using System.Collections.Generic;
using Maple2.Model.Game.Pvp;

namespace Maple2.Server.Tests.Game.Pvp;

// One rank duel match field's best-of rounds, on the state alone.
public class RankDuelMatchStateTests {
    private const long A = 101;
    private const long B = 202;

    private static RankDuelEvent Round => new(RankDuelEventType.Round, Round: 1);
    private static RankDuelEvent RoundOf(int round) => new(RankDuelEventType.Round, Round: round);
    private static RankDuelEvent RoundWon(long winner, long loser, int round) => new(RankDuelEventType.RoundEnd, RankDuelEnd.Win, winner, loser, Round: round);
    private static RankDuelEvent Fight => new(RankDuelEventType.Fight);
    private static RankDuelEvent Exit => new(RankDuelEventType.Exit);
    private static RankDuelEvent Won(long winner, long loser) => new(RankDuelEventType.Result, RankDuelEnd.Win, winner, loser);

    private static RankDuelMatchState Fighting(out long fightTick) {
        var state = new RankDuelMatchState(A, B, 0);
        state.Enter(A, 100);
        state.Enter(B, 200);
        fightTick = 200 + RankDuelMatchState.CountdownMs;
        Assert.That(state.Update(fightTick), Is.EqualTo(new List<RankDuelEvent> { Fight }));
        return state;
    }

    // The loser dies in the round being fought and the next round's fight starts; tick is that fight's start.
    private static void LoseRound(RankDuelMatchState state, long loser, ref long tick) {
        int round = state.Round;
        Assert.That(state.Death(loser, tick + 50), Is.EqualTo(new List<RankDuelEvent> { RoundWon(state.Opponent(loser), loser, round) }));
        Assert.That(state.Update(tick + 50 + RankDuelMatchState.RoundBreakMs), Is.EqualTo(new List<RankDuelEvent> { RoundOf(round + 1) }));
        tick += 50 + RankDuelMatchState.RoundBreakMs + RankDuelMatchState.CountdownMs;
        Assert.That(state.Update(tick), Is.EqualTo(new List<RankDuelEvent> { Fight }));
    }

    [Test]
    public void BothIn_RoundThenFight() {
        var state = new RankDuelMatchState(A, B, 0);
        Assert.That(state.Enter(A, 100), Is.Empty);
        Assert.That(state.Enter(B, 200), Is.EqualTo(new List<RankDuelEvent> { Round }));
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Countdown));
        Assert.That(state.Hostile(A, B), Is.False);
        Assert.That(state.Update(200 + RankDuelMatchState.CountdownMs - 1), Is.Empty);
        Assert.That(state.Update(200 + RankDuelMatchState.CountdownMs), Is.EqualTo(new List<RankDuelEvent> { Fight }));
        Assert.That(state.Hostile(A, B), Is.True);
        Assert.That(state.Hostile(B, A), Is.True);
        Assert.That(state.Hostile(A, A), Is.False);
        Assert.That(state.Hostile(A, 303), Is.False);
    }

    // With a load wait the first round's intro comes that long after the second duelist came, also past the join
    // deadline (both are there: no walkover); one who leaves during the wait loses by walkover.
    [Test]
    public void LoadWait_RoundAfterTheSecondCame() {
        var state = new RankDuelMatchState(A, B, 0, 10_000);
        Assert.That(state.Enter(A, 100), Is.Empty);
        Assert.That(state.Enter(B, 35_000), Is.Empty);
        Assert.That(state.Update(44_999), Is.Empty);
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Waiting));
        Assert.That(state.Hostile(A, B), Is.False);
        Assert.That(state.Update(45_000), Is.EqualTo(new List<RankDuelEvent> { Round }));
        Assert.That(state.Update(45_000 + RankDuelMatchState.CountdownMs), Is.EqualTo(new List<RankDuelEvent> { Fight }));

        var left = new RankDuelMatchState(A, B, 0, 10_000);
        left.Enter(A, 100);
        left.Enter(B, 200);
        Assert.That(left.Leave(A, 5_000), Is.EqualTo(new List<RankDuelEvent> { Won(B, A) }));
        Assert.That(left.WinBy, Is.EqualTo(RankDuelWinBy.Walkover));
    }

    [Test]
    public void SomeoneElse_IsNotADuelist() {
        var state = new RankDuelMatchState(A, B, 0);
        Assert.That(state.Enter(303, 0), Is.Empty);
        Assert.That(state.Enter(A, 0), Is.Empty);
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Waiting));
    }

    // Best of 3 (the observer dialog's two win marks per side): the first death ends round 1 only; the round break,
    // the round 2 intro with its number, then its fight.
    [Test]
    public void FirstDeath_EndsTheRoundOnly() {
        RankDuelMatchState state = Fighting(out long tick);
        Assert.That(state.Death(B, tick + 50), Is.EqualTo(new List<RankDuelEvent> { RoundWon(A, B, 1) }));
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Break));
        Assert.That((state.Wins(A), state.Wins(B), state.Winner), Is.EqualTo((1, 0, 0L)));
        Assert.That(state.Hostile(A, B), Is.False);
        Assert.That(state.Death(A, tick + 60), Is.Empty, "no death counts in the break");
        Assert.That(state.Update(tick + 50 + RankDuelMatchState.RoundBreakMs - 1), Is.Empty);
        Assert.That(state.Update(tick + 50 + RankDuelMatchState.RoundBreakMs), Is.EqualTo(new List<RankDuelEvent> { RoundOf(2) }));
        Assert.That((state.Phase, state.Round), Is.EqualTo((RankDuelPhase.Countdown, 2)));
        Assert.That(state.Update(tick + 50 + RankDuelMatchState.RoundBreakMs + RankDuelMatchState.CountdownMs), Is.EqualTo(new List<RankDuelEvent> { Fight }));
        Assert.That(state.Hostile(A, B), Is.True);
    }

    // 2-1: B takes round 2, A round 3; the match is A's by death.
    [Test]
    public void TwoOne_TheThirdRoundDecides() {
        RankDuelMatchState state = Fighting(out long tick);
        LoseRound(state, B, ref tick);
        LoseRound(state, A, ref tick);
        Assert.That((state.Round, state.Wins(A), state.Wins(B)), Is.EqualTo((3, 1, 1)));
        Assert.That(state.Death(B, tick + 50), Is.EqualTo(new List<RankDuelEvent> { Won(A, B) }));
        Assert.That((state.Winner, state.WinBy, state.Wins(A)), Is.EqualTo((A, RankDuelWinBy.Death, 2)));
    }

    [Test]
    public void DeathInTheFight_Loses() {
        RankDuelMatchState state = Fighting(out long tick);
        LoseRound(state, B, ref tick);
        Assert.That(state.Death(B, tick + 50), Is.EqualTo(new List<RankDuelEvent> { Won(A, B) }));
        Assert.That((state.Winner, state.Loser), Is.EqualTo((A, B)));
        Assert.That(state.Hostile(A, B), Is.False);
        // A second death after the end changes nothing.
        Assert.That(state.Death(A, tick + 60), Is.Empty);
        Assert.That(state.Update(tick + 50 + RankDuelMatchState.ResultMs), Is.EqualTo(new List<RankDuelEvent> { Exit }));
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Closed));
    }

    // Perfect: a death win where no hit of the loser took health from the winner during any round's fight.
    [Test]
    public void Perfect_OnlyAnUntouchedWinnerByDeath() {
        RankDuelMatchState state = Fighting(out long tick);
        state.Hurt(B);
        LoseRound(state, B, ref tick);
        Assert.That(state.Perfect, Is.False);
        state.Death(B, tick + 50);
        Assert.That(state.Perfect, Is.True);
        state.Update(tick + 50 + RankDuelMatchState.ResultMs);
        Assert.That(state.Perfect, Is.True);

        // A hit in round 1 counts against the perfect of round 2's win.
        RankDuelMatchState hurt = Fighting(out tick);
        hurt.Hurt(A);
        LoseRound(hurt, B, ref tick);
        hurt.Death(B, tick + 50);
        Assert.That(hurt.Perfect, Is.False);
        // A hit after the end changes nothing.
        RankDuelMatchState late = Fighting(out tick);
        LoseRound(late, B, ref tick);
        late.Death(B, tick + 50);
        late.Hurt(A);
        Assert.That(late.Perfect, Is.True);
        // A loser that left the fight gives no perfect.
        RankDuelMatchState left = Fighting(out tick);
        left.Leave(B, tick + 50);
        Assert.That(left.Perfect, Is.False);
    }

    [Test]
    public void Perfect_HitsBeforeTheFightDoNotCount() {
        var state = new RankDuelMatchState(A, B, 0);
        state.Enter(A, 100);
        state.Hurt(A);
        state.Enter(B, 200);
        state.Hurt(A);
        long tick = 200 + RankDuelMatchState.CountdownMs;
        state.Update(tick);
        LoseRound(state, B, ref tick);
        state.Death(B, tick + 50);
        Assert.That(state.Perfect, Is.True);
    }

    [Test]
    public void DeathBeforeTheFight_DoesNotCount() {
        var state = new RankDuelMatchState(A, B, 0);
        state.Enter(A, 0);
        Assert.That(state.Death(A, 10), Is.Empty);
        state.Enter(B, 20);
        Assert.That(state.Death(B, 30), Is.Empty);
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Countdown));
    }

    [Test]
    public void LeavingTheFight_Loses() {
        RankDuelMatchState state = Fighting(out long tick);
        Assert.That(state.Leave(A, tick + 1), Is.EqualTo(new List<RankDuelEvent> { Won(B, A) }));
    }

    // A leave between rounds, or in a later round's countdown, is a leave of the fought match, not a walkover.
    [Test]
    public void LeavingBetweenRounds_LosesTheFight() {
        RankDuelMatchState state = Fighting(out long tick);
        state.Death(B, tick + 50);
        Assert.That(state.Leave(A, tick + 60), Is.EqualTo(new List<RankDuelEvent> { Won(B, A) }));
        Assert.That(state.WinBy, Is.EqualTo(RankDuelWinBy.LeftFight));

        RankDuelMatchState later = Fighting(out tick);
        later.Death(B, tick + 50);
        later.Update(tick + 50 + RankDuelMatchState.RoundBreakMs);
        Assert.That(later.Phase, Is.EqualTo(RankDuelPhase.Countdown));
        Assert.That(later.Leave(B, tick + 60 + RankDuelMatchState.RoundBreakMs), Is.EqualTo(new List<RankDuelEvent> { Won(A, B) }));
        Assert.That(later.WinBy, Is.EqualTo(RankDuelWinBy.LeftFight));
    }

    [Test]
    public void LeavingTheCountdown_Loses() {
        var state = new RankDuelMatchState(A, B, 0);
        state.Enter(A, 0);
        state.Enter(B, 0);
        Assert.That(state.Leave(B, 10), Is.EqualTo(new List<RankDuelEvent> { Won(A, B) }));
    }

    [Test]
    public void LeavingAfterTheResult_ChangesNothing() {
        RankDuelMatchState state = Fighting(out long tick);
        LoseRound(state, B, ref tick);
        state.Death(B, tick);
        Assert.That(state.Leave(A, tick + 1), Is.Empty);
        Assert.That(state.Leave(B, tick + 2), Is.Empty);
        Assert.That((state.Winner, state.Loser), Is.EqualTo((A, B)));
    }

    // A round whose fight runs out counts for nobody; with three run out the match is a draw.
    [Test]
    public void FightTimeOut_IsADraw() {
        RankDuelMatchState state = Fighting(out long tick);
        for (int round = 1; round < RankDuelMatchState.MaxRounds; round++) {
            Assert.That(state.Update(tick + RankDuelMatchState.FightMs - 1), Is.Empty);
            Assert.That(state.Update(tick + RankDuelMatchState.FightMs),
                Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.RoundEnd, RankDuelEnd.Draw, Round: round) }));
            tick += RankDuelMatchState.FightMs;
            Assert.That(state.Update(tick + RankDuelMatchState.RoundBreakMs), Is.EqualTo(new List<RankDuelEvent> { RoundOf(round + 1) }));
            tick += RankDuelMatchState.RoundBreakMs + RankDuelMatchState.CountdownMs;
            Assert.That(state.Update(tick), Is.EqualTo(new List<RankDuelEvent> { Fight }));
        }
        Assert.That(state.Round, Is.EqualTo(3));
        Assert.That(state.Update(tick + RankDuelMatchState.FightMs),
            Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.Result, RankDuelEnd.Draw) }));
        Assert.That(state.Winner, Is.EqualTo(0));
    }

    // Round 1 to B, then two rounds run out: B wins on rounds, 1-0, with no perfect.
    [Test]
    public void LastRoundRunsOut_MoreRoundWinsTakeIt() {
        RankDuelMatchState state = Fighting(out long tick);
        LoseRound(state, A, ref tick);
        state.Update(tick + RankDuelMatchState.FightMs);
        tick += RankDuelMatchState.FightMs + RankDuelMatchState.RoundBreakMs;
        state.Update(tick);
        tick += RankDuelMatchState.CountdownMs;
        state.Update(tick);
        Assert.That((state.Round, state.Phase), Is.EqualTo((3, RankDuelPhase.Fighting)));
        Assert.That(state.Update(tick + RankDuelMatchState.FightMs), Is.EqualTo(new List<RankDuelEvent> { Won(B, A) }));
        Assert.That((state.WinBy, state.Wins(B), state.Wins(A), state.Perfect), Is.EqualTo((RankDuelWinBy.Rounds, 1, 0, false)));
    }

    // The round that ran out ends in a break and the next fight; tick is that fight's start.
    private static void RunOut(RankDuelMatchState state, ref long tick) {
        state.Update(tick + RankDuelMatchState.FightMs);
        tick += RankDuelMatchState.FightMs + RankDuelMatchState.RoundBreakMs;
        state.Update(tick);
        tick += RankDuelMatchState.CountdownMs;
        state.Update(tick);
    }

    // A death in round 3 ends the match there, never a round 4: two rounds run out, then B dies: A wins 1-0 by death.
    [Test]
    public void DeathInTheLastRound_EndsTheMatch() {
        RankDuelMatchState state = Fighting(out long tick);
        RunOut(state, ref tick);
        RunOut(state, ref tick);
        Assert.That((state.Round, state.Phase), Is.EqualTo((3, RankDuelPhase.Fighting)));
        Assert.That(state.Death(B, tick + 50), Is.EqualTo(new List<RankDuelEvent> { Won(A, B) }));
        Assert.That((state.WinBy, state.Wins(A), state.Wins(B), state.Round), Is.EqualTo((RankDuelWinBy.Death, 1, 0, 3)));
    }

    // Round 1 to A, round 2 runs out, A dies in round 3: 1-1 after the last round is a draw, not a round 4.
    [Test]
    public void EvenWinsAfterTheLastRound_IsADraw() {
        RankDuelMatchState state = Fighting(out long tick);
        LoseRound(state, B, ref tick);
        RunOut(state, ref tick);
        Assert.That((state.Round, state.Phase), Is.EqualTo((3, RankDuelPhase.Fighting)));
        Assert.That(state.Death(A, tick + 50), Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.Result, RankDuelEnd.Draw) }));
        Assert.That((state.Winner, state.Phase, state.Round), Is.EqualTo((0L, RankDuelPhase.Ended, 3)));
        Assert.That(state.Update(tick + 50 + RankDuelMatchState.ResultMs), Is.EqualTo(new List<RankDuelEvent> { Exit }));
    }

    [Test]
    public void NoShow_LosesToTheOneWhoCame() {
        var state = new RankDuelMatchState(A, B, 1000);
        state.Enter(B, 2000);
        Assert.That(state.Update(1000 + RankDuelMatchState.JoinMs - 1), Is.Empty);
        Assert.That(state.Update(1000 + RankDuelMatchState.JoinMs), Is.EqualTo(new List<RankDuelEvent> { Won(B, A) }));
    }

    [Test]
    public void NobodyCame_IsVoid() {
        var state = new RankDuelMatchState(A, B, 0);
        Assert.That(state.Update(RankDuelMatchState.JoinMs),
            Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.Result, RankDuelEnd.Void) }));
    }

    // The first one in leaves before the other comes: the other wins, and when it comes it is sent out.
    [Test]
    public void LeavingWhileWaiting_Loses() {
        var state = new RankDuelMatchState(A, B, 0);
        state.Enter(A, 0);
        Assert.That(state.Leave(A, 10), Is.EqualTo(new List<RankDuelEvent> { Won(B, A) }));
        Assert.That(state.Enter(B, 20), Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.Dismiss, Character: B) }));
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Ended));
    }

    // A duelist that comes after the result (a late join, a slow load) is sent out at once: after the result and
    // after the exit.
    [Test]
    public void ArrivalAfterTheResult_IsDismissed() {
        var state = new RankDuelMatchState(A, B, 0);
        state.Enter(B, 0);
        state.Update(RankDuelMatchState.JoinMs);
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Ended));
        Assert.That(state.Enter(A, RankDuelMatchState.JoinMs + 1),
            Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.Dismiss, Character: A) }));
        Assert.That(state.IsPresent(A), Is.False);

        state.Update(RankDuelMatchState.JoinMs + RankDuelMatchState.ResultMs);
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Closed));
        Assert.That(state.Enter(A, RankDuelMatchState.JoinMs + RankDuelMatchState.ResultMs + 2000),
            Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.Dismiss, Character: A) }));
        Assert.That(state.Update(RankDuelMatchState.JoinMs + 10 * RankDuelMatchState.ResultMs), Is.Empty);
    }
}

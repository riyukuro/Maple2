using System.Collections.Generic;
using Maple2.Model.Game.Pvp;

namespace Maple2.Server.Tests.Game.Pvp;

// One rank duel match field's round, on the state alone.
public class RankDuelMatchStateTests {
    private const long A = 101;
    private const long B = 202;

    private static RankDuelEvent Round => new(RankDuelEventType.Round);
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

    [Test]
    public void SomeoneElse_IsNotADuelist() {
        var state = new RankDuelMatchState(A, B, 0);
        Assert.That(state.Enter(303, 0), Is.Empty);
        Assert.That(state.Enter(A, 0), Is.Empty);
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Waiting));
    }

    [Test]
    public void DeathInTheFight_Loses() {
        RankDuelMatchState state = Fighting(out long tick);
        Assert.That(state.Death(B, tick + 50), Is.EqualTo(new List<RankDuelEvent> { Won(A, B) }));
        Assert.That((state.Winner, state.Loser), Is.EqualTo((A, B)));
        Assert.That(state.Hostile(A, B), Is.False);
        // A second death after the end changes nothing.
        Assert.That(state.Death(A, tick + 60), Is.Empty);
        Assert.That(state.Update(tick + 50 + RankDuelMatchState.ResultMs), Is.EqualTo(new List<RankDuelEvent> { Exit }));
        Assert.That(state.Phase, Is.EqualTo(RankDuelPhase.Closed));
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
        state.Death(B, tick);
        Assert.That(state.Leave(A, tick + 1), Is.Empty);
        Assert.That(state.Leave(B, tick + 2), Is.Empty);
        Assert.That((state.Winner, state.Loser), Is.EqualTo((A, B)));
    }

    [Test]
    public void FightTimeOut_IsADraw() {
        RankDuelMatchState state = Fighting(out long tick);
        Assert.That(state.Update(tick + RankDuelMatchState.FightMs - 1), Is.Empty);
        Assert.That(state.Update(tick + RankDuelMatchState.FightMs),
            Is.EqualTo(new List<RankDuelEvent> { new(RankDuelEventType.Result, RankDuelEnd.Draw) }));
        Assert.That(state.Winner, Is.EqualTo(0));
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

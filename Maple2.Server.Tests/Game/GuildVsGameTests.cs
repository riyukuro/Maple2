using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game;

// The guild vs game's teams, rounds and rewards (66200001_gd) and its number meshes.
public class GuildVsGameTests {
    [Test]
    public void TeamsByArrival() {
        var state = new GuildVsGameState();
        Assert.That(state.Join(500), Is.EqualTo(GuildVsGameState.BlueTeam));
        Assert.That(state.Join(500), Is.EqualTo(GuildVsGameState.BlueTeam));
        Assert.That(state.Join(0), Is.EqualTo(GuildVsGameState.NoTeam));
        Assert.That(state.Join(700), Is.EqualTo(GuildVsGameState.RedTeam));
        // A third guild is on no team.
        Assert.That(state.Join(900), Is.EqualTo(GuildVsGameState.NoTeam));
        Assert.That(state.TeamOf(900), Is.EqualTo(GuildVsGameState.NoTeam));
        Assert.That(state.TeamOf(700), Is.EqualTo(GuildVsGameState.RedTeam));
        Assert.That(state.GuildOf(GuildVsGameState.BlueTeam), Is.EqualTo(500));
        Assert.That(state.GuildOf(GuildVsGameState.RedTeam), Is.EqualTo(700));
        Assert.That(state.GuildOf(GuildVsGameState.NoTeam), Is.EqualTo(0));
    }

    [Test]
    public void RoundsAndWinner() {
        var state = new GuildVsGameState();
        Assert.That(state.LastScorer, Is.EqualTo(-1));
        Assert.That(state.Winner, Is.EqualTo(GuildVsGameState.NoTeam));

        Assert.That(state.ScoreRound(3, 1, 1), Is.EqualTo(GuildVsGameState.BlueTeam));
        Assert.That(state.LastScorer, Is.EqualTo(GuildVsGameState.BlueTeam));
        Assert.That(state.ScoreRound(2, 2, 1), Is.EqualTo(GuildVsGameState.NoTeam));
        Assert.That(state.LastScorer, Is.EqualTo(GuildVsGameState.NoTeam));
        Assert.That(state.Winner, Is.EqualTo(GuildVsGameState.BlueTeam));

        state.ScoreRound(0, 1, 1);
        Assert.That(state.Winner, Is.EqualTo(GuildVsGameState.NoTeam));
        state.ScoreRound(0, 4, 1);
        Assert.That(state.Score(GuildVsGameState.BlueTeam), Is.EqualTo(1));
        Assert.That(state.Score(GuildVsGameState.RedTeam), Is.EqualTo(2));
        Assert.That(state.Winner, Is.EqualTo(GuildVsGameState.RedTeam));
    }

    [Test]
    public void EachRewardOnce() {
        var state = new GuildVsGameState();
        Assert.That(state.TakeReward(GuildVsGameState.BlueTeam, "exp"), Is.True);
        Assert.That(state.TakeReward(GuildVsGameState.BlueTeam, "exp"), Is.False);
        Assert.That(state.TakeReward(GuildVsGameState.BlueTeam, "fund"), Is.True);
        Assert.That(state.TakeReward(GuildVsGameState.RedTeam, "exp"), Is.True);
        Assert.That(state.TakeReward(GuildVsGameState.NoTeam, "exp"), Is.False);
    }

    [Test]
    public void NumberMeshesShowEachDigit() {
        (int[] shown, int[] hidden) = TriggerContext.NumberMeshes(7, 1000, 2);
        // The ones at the start block, the tens (a leading 0) at start + 10.
        Assert.That(shown, Is.EqualTo(new[] { 1007, 1010 }));
        Assert.That(hidden, Has.Length.EqualTo(18));
        Assert.That(hidden, Does.Not.Contain(1007).And.Not.Contain(1010));

        Assert.That(TriggerContext.NumberMeshes(42, 1000, 2).Shown, Is.EqualTo(new[] { 1002, 1014 }));
        Assert.That(TriggerContext.NumberMeshes(123, 1000, 2).Shown, Is.EqualTo(new[] { 1003, 1012 }));
        Assert.That(TriggerContext.NumberMeshes(-5, 1000, 1).Shown, Is.EqualTo(new[] { 1000 }));
    }
}

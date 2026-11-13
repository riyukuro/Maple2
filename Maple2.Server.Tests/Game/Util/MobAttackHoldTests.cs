using System.Runtime.CompilerServices;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;

namespace Maple2.Server.Tests.Game.Util;

// The GM mob-attack cast holds the mob's AI: a later cast at the same priority cancels the earlier one, so the AI's
// next node would end the GM cast before its first attack point.
public class MobAttackHoldTests {
    private class Cast(TaskState queue) : TaskState.NpcTask(queue, NpcTaskPriority.BattleAction);
    private class Walk(TaskState queue) : TaskState.NpcTask(queue, NpcTaskPriority.BattleWalk);

    private static AiState NewAi() => (AiState) RuntimeHelpers.GetUninitializedObject(typeof(AiState));

    [Test]
    public void SameTierCast_CancelsTheEarlierOne() {
        var queue = new TaskState(null!);
        var gm = new Cast(queue);
        _ = new Cast(queue);

        Assert.That(gm.Status, Is.EqualTo(NpcTaskStatus.Cancelled));
    }

    [Test]
    public void Hold_AiWaitsUntilTheCastEnds() {
        var queue = new TaskState(null!);
        AiState ai = NewAi();
        var gm = new Cast(queue);

        ai.Hold(gm);
        Assert.That(ai.IsWaitingOnTask(long.MaxValue), Is.True);

        gm.Completed();
        Assert.That(ai.IsWaitingOnTask(0), Is.False);
    }

    [Test]
    public void Hold_CancelsTheAiOwnTask() {
        var queue = new TaskState(null!);
        AiState ai = NewAi();
        var walk = new Walk(queue);
        ai.Hold(walk);

        var gm = new Cast(queue);
        ai.Hold(gm);

        Assert.That(walk.Status, Is.EqualTo(NpcTaskStatus.Cancelled));
        Assert.That(gm.IsDone, Is.False);
        ai.Hold(gm);
        Assert.That(gm.IsDone, Is.False);
    }
}

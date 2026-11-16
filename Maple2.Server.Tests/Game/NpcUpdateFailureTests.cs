using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Game;

// What an npc whose update threw is left with (FieldManager.UpdateEach -> MovementState.ResetTasks), and the battle end
// reason a drop with no named reason gives.
public class NpcUpdateFailureTests : NpcCastFixture {
    private static void SetState(FieldNpc npc, ActorState state) =>
        typeof(MovementState).GetField("<State>k__BackingField", Any)!.SetValue(npc.MovementState, state);

    // A hit hold, stun, summon revive, spawn or regen runs out by its own rule; another state goes to idle.
    [TestCase(ActorState.Hit, ActorState.Hit)]
    [TestCase(ActorState.Stun, ActorState.Stun)]
    [TestCase(ActorState.Spawn, ActorState.Spawn)]
    [TestCase(ActorState.Regen, ActorState.Regen)]
    [TestCase(ActorState.Walk, ActorState.Idle)]
    public void ResetTasks_KeepsATimedState(ActorState before, ActorState after) {
        FieldNpc caster = StartCaster(["Idle_A", "Attack_Idle_A"]);
        SetState(caster, before);

        caster.MovementState.ResetTasks();

        Assert.That(caster.MovementState.State, Is.EqualTo(after));
    }

    // A target dropped by a path that names no reason does not carry the previous drop's reason.
    [Test]
    public void ADropWithNoReason_IsDropped() {
        FieldNpc caster = StartCaster(["Idle_A", "Attack_Idle_A"]);
        caster.BattleState.ForceTarget(test.Session.Player);
        typeof(BattleState).GetProperty(nameof(BattleState.LostReason))!.SetValue(caster.BattleState, "target gone");

        typeof(BattleState).GetProperty(nameof(BattleState.Target))!.SetValue(caster.BattleState, null);

        Assert.That(caster.BattleState.LostReason, Is.EqualTo("dropped"));
    }
}

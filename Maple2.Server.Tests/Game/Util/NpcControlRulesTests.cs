using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcControlRulesTests {
    [Test]
    public void BattleFlagOnlyInBattleOrStunned() {
        Assert.That(NpcControlRules.Flags(false, false), Is.EqualTo(0));
        Assert.That(NpcControlRules.Flags(true, false), Is.EqualTo(2));
        Assert.That(NpcControlRules.Flags(false, true), Is.EqualTo(2));
        Assert.That(NpcControlRules.Flags(true, true), Is.EqualTo(2));
    }

    [Test]
    public void CastGoesOutAsStateNone() {
        Assert.That(NpcControlRules.WireState(ActorState.PcSkill), Is.EqualTo(ActorState.None));
        Assert.That((byte) NpcControlRules.WireState(ActorState.PcSkill), Is.EqualTo(0));
        foreach (ActorState state in new[] { ActorState.Idle, ActorState.Walk, ActorState.Jump, ActorState.Stun, ActorState.Talk, ActorState.Regen, ActorState.Spawn }) {
            Assert.That(NpcControlRules.WireState(state), Is.EqualTo(state));
        }
        Assert.That((byte) ActorState.Stun, Is.EqualTo(18));
    }

    [Test]
    public void StunIsSentWithoutASequence() {
        Assert.That(NpcControlRules.WireSequence(ActorState.Stun, 12), Is.EqualTo(-1));
        Assert.That(NpcControlRules.WireSequence(ActorState.Idle, 12), Is.EqualTo(12));
        Assert.That(NpcControlRules.WireSequence(ActorState.None, 12), Is.EqualTo(12));
    }

    [Test]
    public void StunValuesOneAndThreeStun() {
        Assert.That(NpcControlRules.Stuns(0), Is.False);
        Assert.That(NpcControlRules.Stuns(1), Is.True);
        Assert.That(NpcControlRules.Stuns(2), Is.False);
        Assert.That(NpcControlRules.Stuns(3), Is.True);
        Assert.That(NpcControlRules.Stuns(99), Is.False);
    }
}

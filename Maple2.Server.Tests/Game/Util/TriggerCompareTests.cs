using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Game.Util;

public class TriggerCompareTests {
    // npc_hp: shipped scripts test a percent of max hp, mostly compare="lowerEqual" value="5".
    [Test]
    public void NpcHp_ComparesThePercentOfMaxHp() {
        Assert.That(TriggerContext.CompareNpcHp(500, 10000, true, 5, CompareType.lowerEqual), Is.True);
        Assert.That(TriggerContext.CompareNpcHp(501, 10000, true, 5, CompareType.lowerEqual), Is.False);
        Assert.That(TriggerContext.CompareNpcHp(10000, 10000, true, 5, CompareType.lowerEqual), Is.False);
        Assert.That(TriggerContext.CompareNpcHp(5000, 10000, true, 50, CompareType.lower), Is.False);
        Assert.That(TriggerContext.CompareNpcHp(6000, 10000, true, 50, CompareType.higher), Is.True);
        Assert.That(TriggerContext.CompareNpcHp(5000, 10000, true, 50, CompareType.higherEqual), Is.True);
        Assert.That(TriggerContext.CompareNpcHp(40, 10000, false, 50, CompareType.lower), Is.True);
    }

    [Test]
    public void UserValue_UsesTheOperatorAndDefaultsToEqual() {
        Assert.That(TriggerContext.CompareUserValue(1, 1, OperatorType.Equal), Is.True);
        Assert.That(TriggerContext.CompareUserValue(2, 1, OperatorType.Equal), Is.False);
        Assert.That(TriggerContext.CompareUserValue(7, 1, OperatorType.GreaterEqual), Is.True);
        Assert.That(TriggerContext.CompareUserValue(0, 1, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.CompareUserValue(6, 6, OperatorType.LessEqual), Is.True);
        Assert.That(TriggerContext.CompareUserValue(7, 6, OperatorType.LessEqual), Is.False);
    }

    // npc_damage: damage taken over max hp; 02000410 clears at >= 1.0 and fails below it.
    [Test]
    public void NpcDamage_ComparesTheTakenDamageRate() {
        Assert.That(TriggerContext.CompareRate(1.0, 1.0f, OperatorType.GreaterEqual), Is.True);
        Assert.That(TriggerContext.CompareRate(0.99, 1.0f, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.CompareRate(0.99, 1.0f, OperatorType.Less), Is.True);
        Assert.That(TriggerContext.CompareRate(1.2, 1.0f, OperatorType.Less), Is.False);
        Assert.That(TriggerContext.CompareRate(0.5, 0.5f, OperatorType.Equal), Is.True);
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The item option line Bonus Recovery from Allies (receivedhealincrease): a heal from another actor gives its
// holder that much more health. Nothing read it before.
public class ReceivedHealTests {
    [Test]
    public void TheBonusRaisesTheAmountRoundedDown() {
        Assert.That(HealDamageRecord.ReceivedRaised(1000, 0), Is.EqualTo(1000));
        Assert.That(HealDamageRecord.ReceivedRaised(1000, 29), Is.EqualTo(1029));
        Assert.That(HealDamageRecord.ReceivedRaised(245, 39), Is.EqualTo(254));
        // A cut takes its part; -100 % or less leaves nothing; nothing to raise stays nothing.
        Assert.That(HealDamageRecord.ReceivedRaised(1000, -200), Is.EqualTo(800));
        Assert.That(HealDamageRecord.ReceivedRaised(1000, -1500), Is.EqualTo(0));
        Assert.That(HealDamageRecord.ReceivedRaised(0, 29), Is.EqualTo(0));
    }

    private static FieldPlayer Player() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        return test.Session.Player;
    }

    [Test]
    public void OnlyAHealFromAnotherIsRaised() {
        FieldPlayer healer = Player();
        FieldPlayer holder = Player();
        // A flat heal of 1,000 health, 100 spirit and 50 stamina that cannot crit.
        var recovery = new AdditionalEffectMetadataRecovery(RecoveryRate: 0, HpValue: 1000, HpRate: 0, HpConsumeRate: 0,
            SpValue: 100, SpRate: 0, SpConsumeRate: 0, EpValue: 50, EpRate: 0, DisableCrit: true);

        var before = new HealDamageRecord(healer, holder, 1, recovery);
        Assert.That(before.HpAmount, Is.EqualTo(1000));

        // 2.9 % (thousandths, as an item's special rate is kept).
        holder.Stats.Values[SpecialAttribute.BonusRecoveryFromAlly].AddTotal(29);
        var raised = new HealDamageRecord(healer, holder, 1, recovery);
        Assert.That(raised.HpAmount, Is.EqualTo(1029));
        Assert.That(raised.SpAmount, Is.EqualTo(100));
        Assert.That(raised.EpAmount, Is.EqualTo(50));

        // After the caster's invokes and before the stack count: 1,000 x 1.1 = 1,100 -> 1,131, times 2 stacks.
        Assert.That(new HealDamageRecord(healer, holder, 1, recovery, 2, 0.1f).HpAmount, Is.EqualTo(2262));

        // The holder's heal on itself is not.
        Assert.That(new HealDamageRecord(holder, holder, 1, recovery).HpAmount, Is.EqualTo(1000));
        // Nor is the healer's line: it is the receiver's.
        healer.Stats.Values[SpecialAttribute.BonusRecoveryFromAlly].AddTotal(29);
        holder.Stats.Values[SpecialAttribute.BonusRecoveryFromAlly].AddTotal(-29);
        Assert.That(new HealDamageRecord(healer, holder, 1, recovery).HpAmount, Is.EqualTo(1000));
    }
}

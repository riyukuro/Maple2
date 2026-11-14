using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// Special abilities 204 (aevp, True Evasion) and 206 (reduce_do_heal_hp), in thousandths as special rates are kept.
public class TrueEvasionHealDoneTests {
    // The Thief's 3% (aevp 30): a roll under 0.03 evades, one at or over it does not; none evades without the stat.
    [Test]
    public void TrueEvasionEvadesAtItsChance() {
        Assert.That(DamageCalculator.TrueEvade(30, 0.0299), Is.True);
        Assert.That(DamageCalculator.TrueEvade(30, 0.03), Is.False);
        Assert.That(DamageCalculator.TrueEvade(30, 0.5), Is.False);
        Assert.That(DamageCalculator.TrueEvade(0, 0.0), Is.False);
        Assert.That(DamageCalculator.TrueEvade(-10, 0.0), Is.False);
    }

    // The Priest's job change effect (10400251) holds 900: a heal the priest gives keeps a tenth.
    [Test]
    public void HealDoneReductionCutsTheAmount() {
        Assert.That(HealDamageRecord.DoneCut(1000, 900), Is.EqualTo(100));
        Assert.That(HealDamageRecord.DoneCut(1005, 900), Is.EqualTo(101));
        Assert.That(HealDamageRecord.DoneCut(1000, 0), Is.EqualTo(1000));
        Assert.That(HealDamageRecord.DoneCut(1000, 1500), Is.EqualTo(0));
        Assert.That(HealDamageRecord.DoneCut(0, 900), Is.EqualTo(0));
    }

    private static FieldPlayer Player() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        return test.Session.Player;
    }

    // The caster's rate cuts its skill heals, on itself and on others; an item heal (a potion) is not cut, nor is a
    // heal from a caster without it.
    [Test]
    public void TheCastersSkillHealIsCut() {
        FieldPlayer priest = Player();
        FieldPlayer other = Player();
        var recovery = new AdditionalEffectMetadataRecovery(RecoveryRate: 0, HpValue: 1000, HpRate: 0, HpConsumeRate: 0,
            SpValue: 100, SpRate: 0, SpConsumeRate: 0, EpValue: 50, EpRate: 0, DisableCrit: true);
        priest.Stats.Values[SpecialAttribute.HealDoneReduction].AddTotal(900);

        var cut = new HealDamageRecord(priest, other, 1, recovery);
        Assert.That(cut.HpAmount, Is.EqualTo(100));
        Assert.That(cut.SpAmount, Is.EqualTo(100));
        Assert.That(cut.EpAmount, Is.EqualTo(50));
        Assert.That(new HealDamageRecord(priest, priest, 1, recovery).HpAmount, Is.EqualTo(100));
        Assert.That(new HealDamageRecord(priest, priest, 1, recovery, itemHeal: true).HpAmount, Is.EqualTo(1000));
        Assert.That(new HealDamageRecord(other, priest, 1, recovery).HpAmount, Is.EqualTo(1000));
    }
}

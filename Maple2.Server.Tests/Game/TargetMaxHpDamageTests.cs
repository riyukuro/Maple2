using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A hit by target max hp (a skill attack's or a dot's damageByTargetMaxHP) is cut by the caster's Reduce Damage By
// Target Max HP line, the stat Intimidation's debuffs put on the monsters a Berserker hits. Nothing read it before.
public class TargetMaxHpDamageTests {
    private static FieldPlayer Player(int maxHealth) {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.Health].AddTotal(maxHealth - stats[BasicAttribute.Health].Total);
        stats[BasicAttribute.Health].Add(stats[BasicAttribute.Health].Total - stats[BasicAttribute.Health].Current);
        return player;
    }

    [Test]
    public void TheCastersLineCutsTheShare() {
        FieldPlayer caster = Player(5000);
        FieldPlayer target = Player(10000);
        // 10 % of the target's 10000.
        Assert.That(DamageCalculator.TargetMaxHpDamage(caster, target, 0.1f), Is.EqualTo(1000).Within(1));

        // Intimidation's 9 % (thousandths, as a buff's special rate is kept).
        caster.Stats.Values[SpecialAttribute.ReduceDamageByTargetMaxHp].AddTotal(90);
        Assert.That(DamageCalculator.TargetMaxHpDamage(caster, target, 0.1f), Is.EqualTo(910).Within(1));
        // 100 % or more leaves nothing.
        caster.Stats.Values[SpecialAttribute.ReduceDamageByTargetMaxHp].AddTotal(1000);
        Assert.That(DamageCalculator.TargetMaxHpDamage(caster, target, 0.1f), Is.EqualTo(0));
        // No share, no damage.
        Assert.That(DamageCalculator.TargetMaxHpDamage(target, caster, 0f), Is.EqualTo(0));
    }

    [Test]
    public void TheTargetsLineDoesNotCutIt() {
        FieldPlayer caster = Player(5000);
        FieldPlayer target = Player(10000);
        target.Stats.Values[SpecialAttribute.ReduceDamageByTargetMaxHp].AddTotal(500);
        Assert.That(DamageCalculator.TargetMaxHpDamage(caster, target, 0.1f), Is.EqualTo(1000).Within(1));
    }

    [Test]
    public void ADotsShareIsCutToo() {
        FieldPlayer caster = Player(5000);
        FieldPlayer target = Player(10000);
        var dot = new AdditionalEffectMetadataDot.DotDamage(AttackType.None, Element.None, UseGrade: false, Rate: 0,
            HpValue: 0, SpValue: 0, EpValue: 0, DamageByTargetMaxHp: 0.02f, RecoverHpByDamage: 0, IsConstDamage: false,
            NotKill: false);
        Assert.That(new DotDamageRecord(caster, target, dot).HpAmount, Is.EqualTo(-200).Within(1));

        caster.Stats.Values[SpecialAttribute.ReduceDamageByTargetMaxHp].AddTotal(500);
        Assert.That(new DotDamageRecord(caster, target, dot).HpAmount, Is.EqualTo(-100).Within(1));
    }
}

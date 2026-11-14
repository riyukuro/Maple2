using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A dot tick's amounts (DotDamageRecord, negative for damage) from the KR effect rows' dot damage: a constant health
// value, a spirit or stamina value, the stack count and the not-kill cap.
public class DotDamageRowTests {
    private static FieldPlayer Player(int maxHealth, int health) {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.Health].AddTotal(maxHealth - stats[BasicAttribute.Health].Total);
        stats[BasicAttribute.Health].Add(health - stats[BasicAttribute.Health].Current);
        return player;
    }

    private static AdditionalEffectMetadataDot.DotDamage Dot(AttackType type = AttackType.None, bool useGrade = false,
            int hpValue = 0, int spValue = 0, int epValue = 0, float damageByTargetMaxHp = 0, bool isConstDamage = false,
            bool notKill = false) {
        return new AdditionalEffectMetadataDot.DotDamage(type, Element.None, useGrade, Rate: 0, hpValue, spValue, epValue,
            damageByTargetMaxHp, RecoverHpByDamage: 0, isConstDamage, notKill);
    }

    // 70000011 (KR: HpValue 10, IsConstDamage): 10 a tick, times the stack count.
    [Test]
    public void ConstHpValue_HitsForTheValueTimesTheStacks() {
        FieldPlayer caster = Player(5000, 5000);
        FieldPlayer target = Player(10000, 10000);
        AdditionalEffectMetadataDot.DotDamage dot = Dot(hpValue: 10, isConstDamage: true);

        Assert.That(new DotDamageRecord(caster, target, dot).HpAmount, Is.EqualTo(-10));
        Assert.That(new DotDamageRecord(caster, target, dot, stacks: 3).HpAmount, Is.EqualTo(-30));
        Assert.That(new DotDamageRecord(caster, target, dot).SpAmount, Is.EqualTo(0));
    }

    // 10700042 (KR: SpValue 10) takes 10 spirit a tick and no health; 10400031 (KR: EpValue 120) 120 stamina.
    [Test]
    public void SpAndEpValue_TakeSpiritAndStamina() {
        FieldPlayer caster = Player(5000, 5000);
        FieldPlayer target = Player(10000, 10000);

        var sp = new DotDamageRecord(caster, target, Dot(spValue: 10));
        Assert.That(sp.SpAmount, Is.EqualTo(-10));
        Assert.That(sp.HpAmount, Is.EqualTo(0));
        Assert.That(new DotDamageRecord(caster, target, Dot(spValue: 10), stacks: 2).SpAmount, Is.EqualTo(-20));

        var ep = new DotDamageRecord(caster, target, Dot(epValue: 120));
        Assert.That(ep.EpAmount, Is.EqualTo(-120));
        Assert.That(ep.HpAmount, Is.EqualTo(0));
    }

    // 90051039 (KR: Type 3, NotKill, UseGrade, DamageByTargetMaxHp 0.1): a tenth of 10000 on a target with 300 left
    // stops at 1 health.
    [Test]
    public void NotKill_LeavesOneHealth() {
        FieldPlayer caster = Player(5000, 5000);
        FieldPlayer target = Player(10000, 300);

        var record = new DotDamageRecord(caster, target, Dot(AttackType.All, useGrade: true, damageByTargetMaxHp: 0.1f, notKill: true));
        Assert.That(record.HpAmount, Is.EqualTo(-299));
    }
}

using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SuperArmorRulesTests {
    private static SkillMetadataPush Push(int applyField = 0, float up = 0) =>
        new(Type: 1, EaseType: 1, ApplyField: applyField, Distance: 100, UpDistance: up, Down: 0, Fall: false, Duration: 0.2f, Probability: 1, Priority: 0, PriorityHitImmune: 0);

    private static SkillMetadataAttack Attack(bool armorBreak = false, int grab = 0) =>
        (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            Damage = new SkillMetadataDamage(Count: 1, Rate: 1, HitSpeed: 0, HitDelay: 0, IsConstDamage: false, Value: 0,
                DamageByTargetMaxHp: 0, SuperArmorBreak: armorBreak, Push: null),
            GrabTargetType = grab,
        };

    private static SkillMetadataState Cast(SuperArmor armor, int applyField = 0) =>
        new(InBattle: true, SuperArmor: armor, UseInGameTime: false, CooldownGroupId: 0, IgnoreReduceCooldown: false, RechargeMaxCount: 0,
            SuperArmorApplyField: applyField);

    private static AdditionalEffectMetadataProperty Effect(int armor, int applyField = 0) =>
        (AdditionalEffectMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(AdditionalEffectMetadataProperty)) with {
            SuperArmor = armor,
            SuperArmorApplyField = applyField,
        };

    [Test]
    public void PushApplyFieldFollowsTheMap() {
        Assert.That(SuperArmorRules.PushApplies(0, MapType.Pvp), Is.True);
        Assert.That(SuperArmorRules.PushApplies(0, MapType.Dungeon), Is.True);
        Assert.That(SuperArmorRules.PushApplies(1, MapType.Dungeon), Is.True);
        Assert.That(SuperArmorRules.PushApplies(1, MapType.Pvp), Is.False);
        Assert.That(SuperArmorRules.PushApplies(1, MapType.Home), Is.False);
        Assert.That(SuperArmorRules.PushApplies(2, MapType.Dungeon), Is.False);
        Assert.That(SuperArmorRules.PushApplies(2, MapType.Pvp), Is.True);
        Assert.That(SuperArmorRules.PushOn(Push(applyField: 1), MapType.Pvp), Is.Null);
        Assert.That(SuperArmorRules.PushOn(Push() with { Type = 0 }, MapType.Dungeon), Is.Null);
        Assert.That(SuperArmorRules.PushOn(null, MapType.Dungeon), Is.Null);
        Assert.That(SuperArmorRules.PushOn(Push(applyField: 1), MapType.Dungeon), Is.Not.Null);
    }

    [Test]
    public void ArmorApplyFieldFollowsPvp() {
        Assert.That(SuperArmorRules.ArmorCounts(0, 0, MapType.Dungeon), Is.False);
        Assert.That(SuperArmorRules.ArmorCounts(3, 0, MapType.Pvp), Is.True);
        Assert.That(SuperArmorRules.ArmorCounts(3, 1, MapType.Dungeon), Is.True);
        Assert.That(SuperArmorRules.ArmorCounts(3, 1, MapType.Pvp), Is.False);
        Assert.That(SuperArmorRules.ArmorCounts(3, 2, MapType.Pvp), Is.True);
        Assert.That(SuperArmorRules.ArmorCounts(3, 2, MapType.Dungeon), Is.False);
        // only map type 9 counts as PvP here, unlike the push's ground test
        Assert.That(SuperArmorRules.ArmorCounts(3, 2, MapType.Home), Is.False);
    }

    [Test]
    public void ArmorIsTheLargestOfCastAndEffects() {
        Assert.That(SuperArmorRules.Armor(null, [], MapType.Dungeon), Is.EqualTo(0));
        Assert.That(SuperArmorRules.Armor(Cast(SuperArmor.StunImmunity), [], MapType.Dungeon), Is.EqualTo(1));
        Assert.That(SuperArmorRules.Armor(Cast(SuperArmor.StunImmunity), [Effect(3)], MapType.Dungeon), Is.EqualTo(3));
        Assert.That(SuperArmorRules.Armor(Cast(SuperArmor.Full), [Effect(1)], MapType.Dungeon), Is.EqualTo(3));
        // an effect or cast that does not count on the map is left out
        Assert.That(SuperArmorRules.Armor(Cast(SuperArmor.Full, applyField: 2), [Effect(4, applyField: 2), Effect(1)], MapType.Dungeon), Is.EqualTo(1));
        Assert.That(SuperArmorRules.Armor(Cast(SuperArmor.Full, applyField: 1), [Effect(4, applyField: 2)], MapType.Pvp), Is.EqualTo(4));
    }

    [Test]
    public void ArmorLevels() {
        SkillMetadataAttack plain = Attack();
        SkillMetadataAttack grab = Attack(grab: 1);
        Assert.That(SuperArmorRules.Holds(0, plain, null), Is.False);
        // 1: a push or a grab goes through
        Assert.That(SuperArmorRules.Holds(1, plain, null), Is.True);
        Assert.That(SuperArmorRules.Holds(1, plain, Push()), Is.False);
        Assert.That(SuperArmorRules.Holds(1, grab, null), Is.False);
        Assert.That(SuperArmorRules.Holds(1, Attack(grab: 2), null), Is.True);
        // 2: only a push that lifts, or a grab
        Assert.That(SuperArmorRules.Holds(2, plain, Push()), Is.True);
        Assert.That(SuperArmorRules.Holds(2, plain, Push(up: 50)), Is.False);
        Assert.That(SuperArmorRules.Holds(2, grab, null), Is.False);
        // 3: nothing
        Assert.That(SuperArmorRules.Holds(3, grab, Push(up: 50)), Is.True);
        // 4: a push (the push itself) or a grab
        Assert.That(SuperArmorRules.Holds(4, plain, null), Is.True);
        Assert.That(SuperArmorRules.Holds(4, plain, Push()), Is.False);
        Assert.That(SuperArmorRules.Holds(4, grab, null), Is.False);
        // superArmorBreak goes through every level
        Assert.That(SuperArmorRules.Holds(3, Attack(armorBreak: true), null), Is.False);
    }

    [Test]
    public void HeldArmorKeepsTheHitStateOff() {
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, true, ActorState.PcSkill, armorHolds: true), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, false, ActorState.Idle, armorHolds: true), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, true, ActorState.PcSkill, armorHolds: false), Is.True);
    }

    [Test]
    public void BossCastingWithoutArmorStillDoesNotReact() {
        int armor = SuperArmorRules.Armor(Cast(SuperArmor.None), [], MapType.Dungeon);
        Assert.That(armor, Is.EqualTo(0));
        bool holds = SuperArmorRules.Holds(armor, Attack(), Push());
        Assert.That(holds, Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 1, 3, true, ActorState.PcSkill, holds), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 1, 3, true, ActorState.Idle, holds), Is.False);
    }
}

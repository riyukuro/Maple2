using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The Chaos Raid lines (147 to 150) add to the stats and the Dark Stream lines (109, 110) to the damage, each only in
// a room of its dungeon group.
public class DungeonAbilityTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldPlayer Player(DungeonGroupType? group) {
        var test = new TestGameSession();
        test.Start();
        if (group is { } type) {
            DungeonRoomMetadata room = TestGameSession.DungeonRoom(20003001, 2000023) with { GroupType = type };
            test.EnterDungeon(room);
        } else {
            test.EnterMap(TestGameSession.Map(2000023));
        }
        FieldPlayer player = test.Session.Player;
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        // Even weapon attack: no roll between min and max, so two hits compare exactly.
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);
        return player;
    }

    private static readonly DamagePropertyRecord Hit = new() {
        CanCrit = false, Element = Element.None, RangeType = RangeType.None, AttackType = AttackType.Physical,
        Rate = 1, SuperArmorBreak = true,
    };

    [TestCase(70, 0.93)]
    [TestCase(0, 1.0)]
    [TestCase(1000, 0.0)]
    [TestCase(1500, 0.0)]
    [TestCase(-100, 1.0)]
    public void ReceivedFactor(long rate, double factor) {
        Assert.That(DungeonAbilityRules.ReceivedFactor(rate), Is.EqualTo(factor).Within(1e-9));
    }

    [TestCase(DungeonGroupType.chaosRaid, true)]
    [TestCase(DungeonGroupType.darkStream, false)]
    [TestCase(DungeonGroupType.normal, false)]
    [TestCase(null, false)]
    public void ChaosRaidLinesAddToTheStatsOnlyInAChaosRaid(DungeonGroupType? group, bool counts) {
        FieldPlayer player = Player(group);
        Stats stats = player.Stats.Values;
        stats[SpecialAttribute.ChaosRaidAttack].AddTotal(83);
        stats[SpecialAttribute.ChaosRaidAttackSpeed].AddTotal(16);
        stats[SpecialAttribute.ChaosRaidAccuracy].AddTotal(20);
        stats[SpecialAttribute.ChaosRaidHp].AddTotal(500);
        float minRate = stats[BasicAttribute.MinWeaponAtk].Rate;
        float maxRate = stats[BasicAttribute.MaxWeaponAtk].Rate;
        long speed = stats[BasicAttribute.AttackSpeed].Total;
        long accuracy = stats[BasicAttribute.Accuracy].Total;
        long health = stats[BasicAttribute.Health].Total;

        player.Stats.AddDungeonAbilities(player);

        Assert.That(stats[BasicAttribute.MinWeaponAtk].Rate - minRate, Is.EqualTo(counts ? 0.083f : 0f).Within(1e-6));
        Assert.That(stats[BasicAttribute.MaxWeaponAtk].Rate - maxRate, Is.EqualTo(counts ? 0.083f : 0f).Within(1e-6));
        Assert.That(stats[BasicAttribute.AttackSpeed].Total - speed, Is.EqualTo(counts ? 16 : 0));
        Assert.That(stats[BasicAttribute.Accuracy].Total - accuracy, Is.EqualTo(counts ? 20 : 0));
        Assert.That(stats[BasicAttribute.Health].Total - health, Is.EqualTo(counts ? 500 : 0));
    }

    [TestCase(null, DungeonGroupType.chaosRaid, true)]
    [TestCase(DungeonGroupType.chaosRaid, null, true)]
    [TestCase(DungeonGroupType.chaosRaid, DungeonGroupType.darkStream, true)]
    [TestCase(DungeonGroupType.chaosRaid, DungeonGroupType.chaosRaid, false)]
    [TestCase(null, DungeonGroupType.darkStream, false)]
    [TestCase(null, null, false)]
    public void RebuildsOnEntry(DungeonGroupType? left, DungeonGroupType? entered, bool rebuilds) {
        Assert.That(DungeonAbilityRules.RebuildsOnEntry(left, entered), Is.EqualTo(rebuilds));
    }

    [TestCase(DungeonGroupType.darkStream, 1.07)]
    [TestCase(DungeonGroupType.chaosRaid, 1.0)]
    [TestCase(null, 1.0)]
    public void DarkStreamDamageRaisesHitsOnlyInADarkStream(DungeonGroupType? group, double ratio) {
        FieldPlayer caster = Player(group);
        FieldPlayer target = Player(null);
        (_, long plain) = DamageCalculator.CalculateDamage(caster, target, Hit);
        caster.Stats.Values[SpecialAttribute.DarkStreamDamage].AddTotal(70);
        (_, long lined) = DamageCalculator.CalculateDamage(caster, target, Hit);
        Assert.That((double) lined / plain, Is.EqualTo(ratio).Within(0.002));
    }

    [TestCase(DungeonGroupType.darkStream, 0.93)]
    [TestCase(DungeonGroupType.chaosRaid, 1.0)]
    [TestCase(null, 1.0)]
    public void DarkStreamReductionCutsHitsOnlyInADarkStream(DungeonGroupType? group, double ratio) {
        FieldPlayer caster = Player(null);
        FieldPlayer target = Player(group);
        (_, long plain) = DamageCalculator.CalculateDamage(caster, target, Hit);
        target.Stats.Values[SpecialAttribute.ReduceDarkStreamReceiveDamage].AddTotal(70);
        (_, long lined) = DamageCalculator.CalculateDamage(caster, target, Hit);
        Assert.That((double) lined / plain, Is.EqualTo(ratio).Within(0.002));
    }
}

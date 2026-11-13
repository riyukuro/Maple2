using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A target's critical damage resistance (StatusProperty resCadR) scales the caster's critical damage bonus by
// max(1 - rate, 0) before the cap, as the client's own critical damage (CLua::FUN_140674a70) does with its rate. Before,
// the server passed the resistance and ignored it.
public class CriticalDamageResistanceTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [TestCase(1000, 0, 0f, 2f)]
    [TestCase(1000, 0, 0.5f, 1.5f)]
    [TestCase(1000, 0, 0.25f, 1.75f)]
    // The cap applies after the resistance: 3000 is 2.5 either way at a half.
    [TestCase(3000, 0, 0.5f, 2.5f)]
    [TestCase(2000, 0, 0.5f, 2f)]
    // A full (or larger) resistance leaves the plain hit, never less.
    [TestCase(1000, 0, 1f, 1f)]
    [TestCase(1000, 0, 1.5f, 1f)]
    // A negative resistance adds, up to the cap.
    [TestCase(1000, 0, -0.2f, 2.2f)]
    [TestCase(3000, 14, 0.5f, 2.5f)]
    [TestCase(8000, 14, 0.5f, 5f)]
    public void TheResistanceScalesTheBonusBeforeTheCap(int criticalDamage, int mode, float resistance, float expected) {
        Assert.That(Maple2.Server.Game.LuaFunctions.Lua.CalcCritDamage(criticalDamage, mode, resistance), Is.EqualTo(expected).Within(1e-5f));
    }

    [Test]
    public void ACriticalHitOnAResistantTargetTakesTheCutBonus() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        // The test session's stats have no actor; critical damage is read for a player or npc only.
        typeof(StatsManager).GetField("<Actor>k__BackingField", Any)!.SetValue(player.Stats, player);
        // Even weapon attack: no roll between min and max, so two hits compare exactly.
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);
        stats[BasicAttribute.CriticalDamage].AddTotal(1000 - stats[BasicAttribute.CriticalDamage].Total);
        var critical = new DamagePropertyRecord {
            CanCrit = true, AttackType = AttackType.Physical, Rate = 1, SuperArmorBreak = true, CompulsionTypes = [CompulsionType.Critical],
        };
        var normal = new DamagePropertyRecord {
            CanCrit = false, AttackType = AttackType.Physical, Rate = 1, SuperArmorBreak = true,
        };

        long plain = DamageCalculator.CalculateDamage(player, player, normal).Item2;
        (DamageType type, long hit) = DamageCalculator.CalculateDamage(player, player, critical);
        Assert.That(type, Is.EqualTo(DamageType.Critical));
        Assert.That(hit, Is.EqualTo(plain * 2).Within(2));

        // The target holds a quarter of critical damage resistance: 1 + 1000 / 1000 * 0.75.
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
        AdditionalEffectMetadata effect = DisabledBuffTests.Effect(90000001, resistances: new Dictionary<BasicAttribute, float> {
            { BasicAttribute.CriticalDamage, 0.25f },
        });
        Buff buff = DisabledBuffTests.Held(effect, true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, player);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, player);
        buffs[effect.Id] = [buff];
        player.Buffs.EnabledChanged(buff);
        Assert.That(player.Buffs.GetResistance(BasicAttribute.CriticalDamage), Is.EqualTo(0.25f));

        (type, hit) = DamageCalculator.CalculateDamage(player, player, critical);
        Assert.That(type, Is.EqualTo(DamageType.Critical));
        Assert.That(hit, Is.EqualTo(plain * 1.75).Within(2));
    }
}

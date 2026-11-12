using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The item option and buff lines Common Monster Damage, Elite Monster Damage and Bonus Boss Damage each raise hits on
// npcs of their rank only (monsterExpTable's ranks: class 3 and 7 elite, 4 and 6 boss, the rest common). Before, the
// boss line also raised hits on elites, the elite line did nothing and the common line raised every hit.
public class NpcRankDamageTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void EachClassHasItsRank() {
        Assert.That(DamageCalculator.RankBonusOf(0, 1), Is.EqualTo(SpecialAttribute.NormalNpcDamage));
        Assert.That(DamageCalculator.RankBonusOf(0, 3), Is.EqualTo(SpecialAttribute.EliteNpcDamage));
        Assert.That(DamageCalculator.RankBonusOf(0, 7), Is.EqualTo(SpecialAttribute.EliteNpcDamage));
        Assert.That(DamageCalculator.RankBonusOf(0, 4), Is.EqualTo(SpecialAttribute.BossNpcDamage));
        Assert.That(DamageCalculator.RankBonusOf(0, 6), Is.EqualTo(SpecialAttribute.BossNpcDamage));
        Assert.That(DamageCalculator.RankBonusOf(0, 5), Is.EqualTo(SpecialAttribute.NormalNpcDamage));
        // A friendly npc has no rank bonus.
        Assert.That(DamageCalculator.RankBonusOf(2, 6), Is.Null);
        Assert.That(DamageCalculator.RankBonusOf(1, 1), Is.Null);
    }

    // An npc of a class with only what the rank bonus reads: its metadata's class and friendly.
    private static FieldNpc Npc(int npcClass, int friendly = 0) {
        var basic = (NpcMetadataBasic) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataBasic));
        typeof(NpcMetadataBasic).GetField("<Class>k__BackingField", Any)!.SetValue(basic, npcClass);
        typeof(NpcMetadataBasic).GetField("<Friendly>k__BackingField", Any)!.SetValue(basic, friendly);
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Basic>k__BackingField", Any)!.SetValue(metadata, basic);
        var npc = new Npc(metadata, null);
        var fieldNpc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(fieldNpc, npc);
        return fieldNpc;
    }

    [Test]
    public void EachLineRaisesHitsOnItsRankOnly() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Stats stats = player.Stats.Values;
        // Thousandths, as an item's special rate is kept: 1 % common, 2 % elite, 5 % boss.
        stats[SpecialAttribute.NormalNpcDamage].AddTotal(10);
        stats[SpecialAttribute.EliteNpcDamage].AddTotal(20);
        stats[SpecialAttribute.BossNpcDamage].AddTotal(50);

        Assert.That(DamageCalculator.RankBonus(player, Npc(1)), Is.EqualTo(0.01).Within(1e-9));
        Assert.That(DamageCalculator.RankBonus(player, Npc(3)), Is.EqualTo(0.02).Within(1e-9));
        Assert.That(DamageCalculator.RankBonus(player, Npc(7)), Is.EqualTo(0.02).Within(1e-9));
        Assert.That(DamageCalculator.RankBonus(player, Npc(4)), Is.EqualTo(0.05).Within(1e-9));
        Assert.That(DamageCalculator.RankBonus(player, Npc(6)), Is.EqualTo(0.05).Within(1e-9));
        // A friendly npc and a player get none.
        Assert.That(DamageCalculator.RankBonus(player, Npc(6, friendly: 2)), Is.EqualTo(0));
        Assert.That(DamageCalculator.RankBonus(player, player), Is.EqualTo(0));
    }

    [Test]
    public void AHitOnAPlayerIsNotRaisedByTheCommonLine() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        Stats stats = player.Stats.Values;
        stats[BasicAttribute.MinWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MinWeaponAtk].Total);
        stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000 - stats[BasicAttribute.MaxWeaponAtk].Total);
        stats[BasicAttribute.PhysicalAtk].AddTotal(1000);
        var hit = new DamagePropertyRecord {
            CanCrit = false, Element = Element.Fire, RangeType = RangeType.Melee, AttackType = AttackType.Physical,
            Rate = 1, SuperArmorBreak = true,
        };
        (_, long full) = DamageCalculator.CalculateDamage(player, player, hit);
        stats[SpecialAttribute.NormalNpcDamage].AddTotal(500);
        (_, long after) = DamageCalculator.CalculateDamage(player, player, hit);
        Assert.That(after, Is.EqualTo(full).Within(1));
    }
}

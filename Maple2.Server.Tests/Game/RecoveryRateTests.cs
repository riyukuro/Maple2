using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A buff's recovery rates (StatusProperty itemRecoveryHPRate and healRecoveryHPRate) change the health its holder gets
// from potions and from other heals: the effect tooltips read -0.5 as "recovery -50 %". Ingest dropped them before, so
// the boss debuffs that cut potions and heals did nothing.
public class RecoveryRateTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static AdditionalEffectMetadata Effect(int id, float item, float heal) {
        AdditionalEffectMetadata effect = DisabledBuffTests.Effect(id);
        return effect with { Status = effect.Status with { ItemRecoveryRate = item, HealRecoveryRate = heal } };
    }

    [Test]
    public void TheRateChangesTheAmountRoundedDown() {
        Assert.That(HealDamageRecord.RecoveryCut(1000, 0), Is.EqualTo(1000));
        Assert.That(HealDamageRecord.RecoveryCut(1000, -0.5f), Is.EqualTo(500));
        Assert.That(HealDamageRecord.RecoveryCut(1000, -0.95f), Is.EqualTo(50));
        Assert.That(HealDamageRecord.RecoveryCut(245, -0.33f), Is.EqualTo(164));
        // 90060009 raises potions by 20 %.
        Assert.That(HealDamageRecord.RecoveryCut(1000, 0.2f), Is.EqualTo(1200));
        // -100 % or less leaves nothing; nothing stays nothing.
        Assert.That(HealDamageRecord.RecoveryCut(1000, -1f), Is.EqualTo(0));
        Assert.That(HealDamageRecord.RecoveryCut(1000, -1.2f), Is.EqualTo(0));
        Assert.That(HealDamageRecord.RecoveryCut(0, -0.5f), Is.EqualTo(0));
    }

    [Test]
    public void APotionHealIsOfSubTypeConsumable() {
        Assert.That(Buff.IsItemHeal(BuffSubType.Consumable), Is.True);
        Assert.That(Buff.IsItemHeal(BuffSubType.Consumable | BuffSubType.Buff), Is.True);
        Assert.That(Buff.IsItemHeal(BuffSubType.Recovery), Is.False);
        Assert.That(Buff.IsItemHeal(BuffSubType.None), Is.False);
    }

    [Test]
    public void TheRatesOfTheBuffsHeldAddUpByStacks() {
        // 70000071 at level 1 (both -0.1), 50004803 (potions -0.2, up to 5 stacks) at 3.
        var sums = new BuffSums([
            (Effect(70000071, -0.1f, -0.1f), 1),
            (Effect(50004803, -0.2f, 0), 3),
            (Effect(90060009, 0.2f, 0), 0),
        ]);
        Assert.That(sums.ItemRecovery, Is.EqualTo(-0.7f).Within(1e-6f));
        Assert.That(sums.HealRecovery, Is.EqualTo(-0.1f).Within(1e-6f));

        // A buff whose begin condition fails counts for nothing.
        BuffSums some = BuffManager.SumsOf([DisabledBuffTests.Held(Effect(49113001, -0.5f, -0.5f), false), DisabledBuffTests.Held(Effect(50004800, -0.8f, 0), true)]);
        Assert.That(some.ItemRecovery, Is.EqualTo(-0.8f).Within(1e-6f));
        Assert.That(some.HealRecovery, Is.EqualTo(0));
    }

    [Test]
    public void AHealOnTheHolderIsCutByItsRate() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        FieldPlayer player = test.Session.Player;
        // A flat heal of 1,000 health, 100 spirit and 50 stamina that cannot crit.
        var recovery = new AdditionalEffectMetadataRecovery(RecoveryRate: 0, HpValue: 1000, HpRate: 0, HpConsumeRate: 0,
            SpValue: 100, SpRate: 0, SpConsumeRate: 0, EpValue: 50, EpRate: 0, DisableCrit: true);
        Assert.That(player.Buffs.RecoveryRate(true), Is.EqualTo(0));
        Assert.That(new HealDamageRecord(player, player, 1, recovery, 1, 0f, player.Buffs.RecoveryRate(true)).HpAmount, Is.EqualTo(1000));

        // 50004800 at level 1: potions -80 %; other heals unchanged.
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(player.Buffs)!;
        Buff buff = DisabledBuffTests.Held(Effect(50004800, -0.8f, 0), true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, player);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, player);
        buffs[buff.Id] = [buff];
        player.Buffs.EnabledChanged(buff);

        var potion = new HealDamageRecord(player, player, 1, recovery, 1, 0f, player.Buffs.RecoveryRate(true));
        Assert.That(potion.HpAmount, Is.EqualTo(200));
        Assert.That(potion.SpAmount, Is.EqualTo(100));
        Assert.That(potion.EpAmount, Is.EqualTo(50));
        Assert.That(new HealDamageRecord(player, player, 1, recovery, 1, 0f, player.Buffs.RecoveryRate(false)).HpAmount, Is.EqualTo(1000));

        // Before the stack count: 1,000 x 0.2 = 200, times 2 stacks.
        Assert.That(new HealDamageRecord(player, player, 1, recovery, 2, 0f, player.Buffs.RecoveryRate(true)).HpAmount, Is.EqualTo(400));
    }
}

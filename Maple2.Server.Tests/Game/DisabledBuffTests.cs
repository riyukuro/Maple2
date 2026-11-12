using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A held buff whose begin condition fails (Buff.Enabled false) counts for no stat, resistance, invoke, invincible or
// stun, as the client's sums skip a buff without m_bEnabled (AdditionalCliList::FUN_140972a00).
public class DisabledBuffTests {
    private static readonly AdditionalEffectMetadataProperty Property = new(BuffType.Buff, BuffSubType.Status, BuffCategory.None,
        BuffEventType.None, 0, 1000, 0, 0, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, 0,
        BuffKeepCondition.TimerDuration, default, default, 0);

    internal static AdditionalEffectMetadata Effect(int id, Dictionary<BasicAttribute, long>? values = null,
            Dictionary<SpecialAttribute, float>? specialRates = null, Dictionary<BasicAttribute, float>? resistances = null,
            AdditionalEffectMetadataInvokeEffect? invoke = null, bool invincible = false, AdditionalEffectMetadataProperty? property = null,
            Dictionary<BasicAttribute, float>? rates = null, AdditionalEffectMetadataStatus.CompulsionEvent? compulsion = null,
            AdditionalEffectMetadataShield? shield = null, bool deathResistance = false) {
        var status = new AdditionalEffectMetadataStatus(values ?? new Dictionary<BasicAttribute, long>(), rates ?? new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), specialRates ?? new Dictionary<SpecialAttribute, float>(),
            resistances ?? new Dictionary<BasicAttribute, float>(), deathResistance, compulsion, null, 0, invincible);
        return new AdditionalEffectMetadata(id, 1, null!, property ?? Property, null!, null!, null!, status, null, null!, shield, invoke, [], [], []);
    }

    // A held buff as the field keeps it, with only what the sums read: its data, stack count and enabled flag.
    internal static Buff Held(AdditionalEffectMetadata metadata, bool enabled, int stacks = 1) {
        var buff = (Buff) RuntimeHelpers.GetUninitializedObject(typeof(Buff));
        typeof(Buff).GetField(nameof(Buff.Metadata))!.SetValue(buff, metadata);
        typeof(Buff).GetField("<Stacks>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(buff, stacks);
        typeof(Buff).GetField("<Enabled>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(buff, enabled);
        return buff;
    }

    private static Stats Player() => new(new Dictionary<BasicAttribute, long> { { BasicAttribute.PhysicalRes, 100 } }, 0);

    [Test]
    public void ADisabledBuffGivesNoStats() {
        // 10100232: physical and magical res +150 while HP is at most 30 %; 10100231: offensive damage +10 % at 70 % and more.
        AdditionalEffectMetadata low = Effect(10100232, values: new Dictionary<BasicAttribute, long> { { BasicAttribute.PhysicalRes, 150 } });
        AdditionalEffectMetadata high = Effect(10100231, specialRates: new Dictionary<SpecialAttribute, float> { { SpecialAttribute.OffensivePhysicalDamage, 0.1f } });

        Stats stats = Player();
        StatsManager.AddBuffs(stats, [Held(low, false), Held(high, true)]);
        stats.Total();
        Assert.That(stats[BasicAttribute.PhysicalRes].Total, Is.EqualTo(100));
        Assert.That(stats[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(100));

        // The HP drops under 30 %: the bands swap.
        stats = Player();
        StatsManager.AddBuffs(stats, [Held(low, true), Held(high, false)]);
        stats.Total();
        Assert.That(stats[BasicAttribute.PhysicalRes].Total, Is.EqualTo(250));
        Assert.That(stats[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(0));
    }

    [Test]
    public void ADisabledBuffGivesNoInvokeDamageBonus() {
        // 90050806 adds 20 thousandths to 90050805's physical damage; held disabled, it adds nothing.
        var invoke = new AdditionalEffectMetadataInvokeEffect([InvokeEffectType.IncreaseOffensivePhysicalDamage], [20f], [0f], EffectId: 90050805, EffectGroupId: 0,
            SkillId: 0, SkillGroupId: 0);
        AdditionalEffectMetadata named = Effect(90050805);
        AdditionalEffectMetadata bonus = Effect(90050806, invoke: invoke);

        Stats on = Player();
        StatsManager.AddBuffs(on, [Held(named, true), Held(bonus, true)]);
        on.Total();
        Assert.That(on[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(20));

        Stats off = Player();
        StatsManager.AddBuffs(off, [Held(named, true), Held(bonus, false)]);
        off.Total();
        Assert.That(off[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(0));
    }

    [Test]
    public void ADisabledBuffIsNotInTheSums() {
        var invoke = new AdditionalEffectMetadataInvokeEffect([InvokeEffectType.IncreaseSkillDamage], [0f], [0.1f], EffectId: 0, EffectGroupId: 0,
            SkillId: 10500001, SkillGroupId: 0);
        AdditionalEffectMetadataProperty stun = Property with { Stun = 1 };
        AdditionalEffectMetadata resist = Effect(1, resistances: new Dictionary<BasicAttribute, float> { { BasicAttribute.Accuracy, 0.1f } });
        AdditionalEffectMetadata invoking = Effect(2, invoke: invoke);
        AdditionalEffectMetadata invincible = Effect(3, invincible: true);
        AdditionalEffectMetadata stunning = Effect(4, property: stun);

        BuffSums off = BuffManager.SumsOf([Held(resist, false, 2), Held(invoking, false), Held(invincible, false), Held(stunning, false)]);
        Assert.That(off.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0f));
        Assert.That(off.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001), Is.EqualTo((0, 0f)));
        Assert.That(off.Invincible, Is.False);
        Assert.That(off.Stun, Is.False);

        BuffSums on = BuffManager.SumsOf([Held(resist, true, 2), Held(invoking, true), Held(invincible, true), Held(stunning, true)]);
        Assert.That(on.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0.2f).Within(1e-6f));
        Assert.That(on.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001).Item2, Is.EqualTo(0.1f).Within(1e-6f));
        Assert.That(on.Invincible, Is.True);
        Assert.That(on.Stun, Is.True);
    }

    [Test]
    public void ADisabledBuffGivesNoCompulsionRate() {
        // 10300242: critical override 1 on three skills while the owner holds 10300250; 10300201: block 0.1 on every skill.
        AdditionalEffectMetadata crit = Effect(10300242, compulsion: new AdditionalEffectMetadataStatus.CompulsionEvent(
            BuffCompulsionEventType.CritChanceOverride, 1f, [10300222, 10300225, 10300226]));
        AdditionalEffectMetadata block = Effect(10300201, compulsion: new AdditionalEffectMetadataStatus.CompulsionEvent(
            BuffCompulsionEventType.BlockChance, 0.1f, []));

        Buff[] on = [Held(crit, true), Held(block, true)];
        Assert.That(BuffManager.CompulsionRate(on, BuffCompulsionEventType.CritChanceOverride, 10300222), Is.EqualTo(1f));
        Assert.That(BuffManager.CompulsionRate(on, BuffCompulsionEventType.CritChanceOverride, 10300001), Is.EqualTo(0f));
        Assert.That(BuffManager.CompulsionRate(on, BuffCompulsionEventType.BlockChance, 10300001), Is.EqualTo(0.1f).Within(1e-6f));

        Buff[] off = [Held(crit, false), Held(block, true)];
        Assert.That(BuffManager.CompulsionRate(off, BuffCompulsionEventType.CritChanceOverride, 10300222), Is.EqualTo(0f));
        Assert.That(BuffManager.CompulsionRate(off, BuffCompulsionEventType.BlockChance), Is.EqualTo(0.1f).Within(1e-6f));

        // One buff id counts once, from any number of casters.
        Assert.That(BuffManager.CompulsionRate([Held(block, true), Held(block, true)], BuffCompulsionEventType.BlockChance), Is.EqualTo(0.1f).Within(1e-6f));
    }

    [Test]
    public void ADisabledShieldTakesNoHit() {
        // 61100121: a 1200 shield while the owner holds 61100120; 90060012: 30 % of the holder's health.
        AdditionalEffectMetadata held = Effect(61100121, shield: new AdditionalEffectMetadataShield(1200, 0f));
        AdditionalEffectMetadata share = Effect(90060012, shield: new AdditionalEffectMetadataShield(0, 0.3f));
        AdditionalEffectMetadata plain = Effect(1);
        Buff on = Held(held, true);
        Buff offShare = Held(share, false);

        Assert.That(BuffManager.Shields([on, offShare, Held(plain, true)]), Is.EqualTo(new[] { on }));
        Assert.That(BuffManager.Shields([Held(held, false), offShare]), Is.Empty);
    }

    [Test]
    public void ADisabledBuffDoesNotBlockARevive() {
        // 50005500 (disableRevivalHere): the client takes the flag from enabled buffs only.
        AdditionalEffectMetadata noRevive = Effect(50005500, property: Property with { DisableRevivalHere = true });
        Assert.That(BuffManager.BlocksRevival([Held(noRevive, true)]), Is.True);
        Assert.That(BuffManager.BlocksRevival([Held(noRevive, false)]), Is.False);
        Assert.That(BuffManager.BlocksRevival([Held(Effect(1), true)]), Is.False);
    }

    [Test]
    public void ABuffOrAFieldDisablesSkillMainTypes() {
        // 50002100: skillUseDisable 4 (bit 2, the job skills, main type 2 as 10000001); field 51000001 lists 1, 2, 3, 5, 8, 9.
        AdditionalEffectMetadata jobBlock = Effect(50002100, property: Property with { SkillUseDisableMask = 4 });
        Assert.That(BuffManager.SkillUseDisableMask([Held(jobBlock, true), Held(Effect(1), true)]), Is.EqualTo(4));
        Assert.That(BuffManager.SkillUseDisableMask([Held(jobBlock, false)]), Is.EqualTo(0));
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.TypeDisabled(4, 0, 2), Is.True);
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.TypeDisabled(4, 0, 4), Is.False);
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.TypeDisabled(0, 0, 2), Is.False);
        // A later motion of the same cast is not checked, as the client sends it without the check.
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.TypeDisabled(4, 0, 2, laterMotion: true), Is.False);
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.TypeDisabled(4, 0, 2, laterMotion: false), Is.True);
    }

    [Test]
    public void OnlyAHeldCastHasLaterMotions() {
        // The next motion of a held cast of 10200001 (motion 0 taken, then motion 1 sent).
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.LaterMotion(1, 10200001, 10200001, 0), Is.True);
        // A Use with motion point 1 and no cast of that id held: a cast start, checked.
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.LaterMotion(1, 10200001, null, 0), Is.False);
        // The cast id held is of another skill.
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.LaterMotion(1, 10200001, 10200011, 0), Is.False);
        // Not past the motion held.
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.LaterMotion(1, 10200001, 10200001, 1), Is.False);
        // Motion 0 is always a cast start.
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.LaterMotion(0, 10200001, 10200001, 0), Is.False);

        int field = Maple2.Server.Game.Util.SkillCastRules.FieldTypeMask([1, 2, 3, 5, 8, 9]);
        Assert.That(field, Is.EqualTo(0b1100101110));
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.TypeDisabled(0, field, 2), Is.True);
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.TypeDisabled(0, field, 4), Is.False);
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.FieldTypeMask([]), Is.EqualTo(0));
        Assert.That(Maple2.Server.Game.Util.SkillCastRules.FieldTypeMask(null), Is.EqualTo(0));
    }

    [Test]
    public void ABuffEnabledLateProcsOnItsSchedule() {
        // Due at 1000 every 500, back on at 2200: the next proc is 2500, the missed ones (1000 to 2000) are not paid.
        Assert.That(Buff.ResumedProcTick(1000, 500, 2200), Is.EqualTo(2500));
        Assert.That(Buff.ResumedProcTick(1000, 500, 2000), Is.EqualTo(2000));
        // Not yet due, or a buff of one proc (no interval): unchanged, so it still procs once it is on.
        Assert.That(Buff.ResumedProcTick(3000, 500, 2200), Is.EqualTo(3000));
        Assert.That(Buff.ResumedProcTick(1000, 0, 2200), Is.EqualTo(1000));
    }

    [Test]
    public void ABuffEnabledAfterItsLastProcSlotDoesNotProcPastItsEnd() {
        // Due at 5000 every 1000, ends at 5500, back on at 5200: the next slot (6000) is past the end, so no proc.
        Assert.That(Buff.ResumedCanProc(Buff.ResumedProcTick(5000, 1000, 5200), 5500), Is.False);
        // Back on before the slot: it still procs at 5000.
        Assert.That(Buff.ResumedCanProc(Buff.ResumedProcTick(5000, 1000, 4800), 5500), Is.True);
        Assert.That(Buff.ResumedCanProc(5500, 5500), Is.True);
    }
}

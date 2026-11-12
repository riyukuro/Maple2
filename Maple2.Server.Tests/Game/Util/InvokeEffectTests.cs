using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model.Skill;

namespace Maple2.Server.Tests.Game.Util;

// The invoke effects of the played client's additional data, and what the effect-keyed types change.
public class InvokeEffectTests {
    private const string Played = """
        <ms2>
          <additional id="10100241">
            <level level="1"><Basic durationTick="0" /><InvokeEffect skillGroupID="10100241" types="1004" values="0" rates="0.23" /></level>
          </additional>
          <additional id="10400281">
            <level level="1"><Basic /><InvokeEffect effectID="10400182" types="4" values="10000" rates="0" /></level>
            <level level="2" feature="JobChange_01"><Basic /></level>
          </additional>
          <additional id="10400212">
            <level level="1"><Basic durationTick="100" intervalTick="100" groupIDs="10400301" /></level>
            <level level="2"><Basic groupIDs="5" /></level>
          </additional>
          <additional id="90050806">
            <level level="1"><Basic /><InvokeEffect effectID="90050805" types="38" values="20" /></level>
          </additional>
          <additional id="11000102">
            <level level="1"><InvokeEffect skillID="11000001" types="1001,1002,1003" values="0,4" rates="0.05" /></level>
          </additional>
        </ms2>
        """;

    private static Dictionary<int, PlayedClientAdditionalEffects.Entry> Read() {
        var document = new XmlDocument();
        document.LoadXml(Played);
        return PlayedClientAdditionalEffects.Read([document]);
    }

    private static AdditionalEffectMetadataInvokeEffect Invoke(InvokeEffectType type, float value, float rate,
            int effectId = 0, int effectGroupId = 0, int skillId = 0, int skillGroupId = 0) {
        return new AdditionalEffectMetadataInvokeEffect([type], [value], [rate], effectId, effectGroupId, skillId, skillGroupId);
    }

    [Test]
    public void ReadsTheClientRowsInTheServerNumbers() {
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = Read();
        AdditionalEffectMetadataInvokeEffect damage = played[10100241].Levels[1]!;
        Assert.That(damage.Types, Is.EqualTo(new[] { InvokeEffectType.IncreaseSkillDamage }));
        Assert.That(damage.Rates[0], Is.EqualTo(0.23f).Within(1e-6f));
        Assert.That(damage.SkillGroupId, Is.EqualTo(10100241));

        // Client type 4 is a duration in milliseconds: IncreaseEffectDuration by value.
        AdditionalEffectMetadataInvokeEffect duration = played[10400281].Levels[1]!;
        Assert.That(duration.Types, Is.EqualTo(new[] { InvokeEffectType.IncreaseEffectDuration }));
        Assert.That(duration.Values[0], Is.EqualTo(10000f));
        Assert.That(duration.EffectId, Is.EqualTo(10400182));
        // A level the client has without an invoke has none.
        Assert.That(played[10400281].Levels.ContainsKey(2), Is.True);
        Assert.That(played[10400281].Levels[2], Is.Null);

        // The effect group is the first level's groupIDs.
        Assert.That(played[10400212].GroupId, Is.EqualTo(10400301));
        Assert.That(played[10400281].GroupId, Is.EqualTo(0));

        Assert.That(played[90050806].Levels[1]!.Types, Is.EqualTo(new[] { InvokeEffectType.IncreaseOffensivePhysicalDamage }));
        Assert.That(played[90050806].Levels[1]!.Values[0], Is.EqualTo(20f));
        Assert.That(played[90050806].Levels[1]!.Rates, Is.EqualTo(new[] { 0f }));

        // Short value and rate lists are padded with 0 to the type count.
        AdditionalEffectMetadataInvokeEffect cooldown = played[11000102].Levels[1]!;
        Assert.That(cooldown.Types, Is.EqualTo(new[] { InvokeEffectType.ReduceCooldown, InvokeEffectType.ReduceCooldown, InvokeEffectType.ReduceSpiritCost }));
        Assert.That(cooldown.Values, Is.EqualTo(new[] { 0f, 4f, 0f }));
        Assert.That(cooldown.Rates, Is.EqualTo(new[] { 0.05f, 0f, 0f }));
    }

    [Test]
    public void ClientTypes() {
        Assert.That(PlayedClientAdditionalEffects.ClientType(1001), Is.EqualTo(InvokeEffectType.ReduceCooldown));
        Assert.That(PlayedClientAdditionalEffects.ClientType(1002), Is.EqualTo(InvokeEffectType.ReduceCooldown));
        Assert.That(PlayedClientAdditionalEffects.ClientType(1003), Is.EqualTo(InvokeEffectType.ReduceSpiritCost));
        Assert.That(PlayedClientAdditionalEffects.ClientType(1004), Is.EqualTo(InvokeEffectType.IncreaseSkillDamage));
        Assert.That(PlayedClientAdditionalEffects.ClientType(4), Is.EqualTo(InvokeEffectType.IncreaseEffectDuration));
        Assert.That(PlayedClientAdditionalEffects.ClientType(58), Is.EqualTo(InvokeEffectType.IncreaseHealing));
        Assert.That((int) InvokeEffectType.IncreaseOffensivePhysicalDamage, Is.EqualTo(38));
        Assert.That((int) InvokeEffectType.IncreaseOffensiveMagicalDamage, Is.EqualTo(40));
    }

    [Test]
    public void EffectInvokesCountForTheirEffectOrGroupOnly() {
        (AdditionalEffectMetadataInvokeEffect?, int)[] held = [
            (Invoke(InvokeEffectType.IncreaseEffectDuration, 10000f, 0f, effectId: 10400182), 1),
            (Invoke(InvokeEffectType.IncreaseEffectDuration, 0f, 0.3f, effectId: 10400182), 2),
            (Invoke(InvokeEffectType.IncreaseHealing, 0f, 0.04f, effectGroupId: 10400301), 1),
            // Keyed on a skill: an effect's sums never count it, whatever effect id it also carries.
            (Invoke(InvokeEffectType.IncreaseEffectDuration, 500f, 0f, effectId: 10400182, skillId: 10400182), 1),
            (null, 1),
        ];
        (int value, float rate) = BuffManager.SumEffectInvokes(held, InvokeEffectType.IncreaseEffectDuration, 10400182, 0);
        Assert.That(value, Is.EqualTo(10000));
        Assert.That(rate, Is.EqualTo(0.6f).Within(1e-6f));
        Assert.That(BuffManager.SumEffectInvokes(held, InvokeEffectType.IncreaseEffectDuration, 10400183, 0), Is.EqualTo((0, 0f)));

        Assert.That(BuffManager.SumEffectInvokes(held, InvokeEffectType.IncreaseHealing, 10400212, 10400301).Item2, Is.EqualTo(0.04f).Within(1e-6f));
        Assert.That(BuffManager.SumEffectInvokes(held, InvokeEffectType.IncreaseHealing, 10400212, 0), Is.EqualTo((0, 0f)));

        // The skill sums count the skill-keyed row only, not an effect's row of the same number.
        Assert.That(BuffManager.SumInvokes(held, InvokeEffectType.IncreaseEffectDuration, 10400182), Is.EqualTo((500, 0f)));
    }

    [Test]
    public void BuffSumsMatchTheEffectWalk() {
        AdditionalEffectMetadataInvokeEffect invoke = Invoke(InvokeEffectType.IncreaseHealing, 0f, 0.06f, effectId: 11100035);
        var property = new AdditionalEffectMetadataProperty(BuffType.Buff, BuffSubType.Status, BuffCategory.None, BuffEventType.None,
            0, 1000, 0, 0, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, 0, BuffKeepCondition.TimerDuration, default, default, 0);
        var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null, null, 0, false, false);
        var effect = new AdditionalEffectMetadata(1, 1, null!, property, null!, null!, null!, status, null, null!, null, invoke, [], [], []);
        var sums = new BuffSums([(effect, 2)]);
        Assert.That(sums.EffectInvokes(InvokeEffectType.IncreaseHealing, 11100035, 0).Item2, Is.EqualTo(0.12f).Within(1e-6f));
        Assert.That(sums.EffectInvokes(InvokeEffectType.IncreaseHealing, 11100036, 0), Is.EqualTo((0, 0f)));
    }

    [Test]
    public void DamageInvokesRaiseTheNamedEffectWhileItIsHeld() {
        AdditionalEffectMetadataInvokeEffect physical = Invoke(InvokeEffectType.IncreaseOffensivePhysicalDamage, 20f, 0f, effectId: 90050805);
        AdditionalEffectMetadataInvokeEffect magical = Invoke(InvokeEffectType.IncreaseOffensiveMagicalDamage, 52f, 0f, effectId: 90050825);

        List<(SpecialAttribute Attribute, float Rate)> bonus = StatsManager.InvokeDamageBonus(
            [(physical, 3), (magical, 1)], [(90050805, 0), (90050806, 0), (90050825, 0)]);
        Assert.That(bonus.Select(entry => entry.Attribute), Is.EqualTo(new[] { SpecialAttribute.OffensivePhysicalDamage, SpecialAttribute.OffensiveMagicalDamage }));
        Assert.That(bonus[0].Rate, Is.EqualTo(0.06f).Within(1e-6f));
        Assert.That(bonus[1].Rate, Is.EqualTo(0.052f).Within(1e-6f));

        // Without the named effect, or with the data pack's empty value, there is no bonus.
        Assert.That(StatsManager.InvokeDamageBonus([(physical, 3)], [(90050806, 0)]), Is.Empty);
        Assert.That(StatsManager.InvokeDamageBonus([(Invoke(InvokeEffectType.IncreaseOffensivePhysicalDamage, 0f, 1f, effectId: 90050805), 1)], [(90050805, 0)]), Is.Empty);
        // Other types add no stat.
        Assert.That(StatsManager.InvokeDamageBonus([(Invoke(InvokeEffectType.IncreaseHealing, 20f, 0f, effectId: 90050805), 1)], [(90050805, 0)]), Is.Empty);
    }

    [Test]
    public void HealingRaisedByTheRate() {
        Assert.That(HealDamageRecord.HealingRaised(1000, 0f), Is.EqualTo(1000));
        Assert.That(HealDamageRecord.HealingRaised(1000, 0.04f), Is.EqualTo(1040));
        Assert.That(HealDamageRecord.HealingRaised(245, 0.06f), Is.EqualTo(259));
    }
}

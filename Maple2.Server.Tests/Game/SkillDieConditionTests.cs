using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The quest condition of a death by a map's or a monster's skill, from a real KR row, through FieldPlayer.ApplyDamage.
public class SkillDieConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        // The fixture's actor is not constructed: what a hit reads is set here (as HeldHitHealthTests).
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(player, new ConcurrentDictionary<int, DamageRecordTarget>());
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(player, new RegenClock());
        Stat health = player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(1000 - health.Total);
        health.Add(1000 - health.Current);
    }

    // A hit of a fixed amount by the map's skill of this id.
    private void Hit(FieldActor caster, int skillId, long amount) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(skillId, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: amount,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        player.ApplyDamage(caster, new DamageRecord(skill, attack), attack);
    }

    // 21233007: <condition type="skill_die" code="70000500" value="1" />: the hit of that skill that takes the last
    // health counts; a hit that leaves health, or the killing hit of another skill, does not.
    [TestCase(70000500, 1)]
    [TestCase(70000501, 0)]
    public void SkillDie_21233007_TheKillingHitOfTheSkill(int killer, int counted) {
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.skill_die, 1, new ConditionMetadata.Parameters(Integers: [70000500]), null)],
        });
        using var caster = new FieldActor(test.Session.Field!, 99, test.Session.Field!.Metadata, null!);

        Hit(caster, 70000500, 100);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Hit(caster, killer, 5000);

        Assert.That(player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(0));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counted));
    }
}

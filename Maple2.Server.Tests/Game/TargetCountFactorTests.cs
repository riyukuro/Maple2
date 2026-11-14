using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// useTargetCountFactor of a proc's begin condition (KR: the conditionSkill rows of 10200205, 10200206, 10700001,
// 10700002, 10700181 and 10700182): the probability is rolled times the server constant
// invokeEffectTargetCountFactor<n>, n the number of targets the attack hit.
public class TargetCountFactorTests {
    // The KR server table's invokeEffectTargetCountFactor1-10.
    private static readonly ConstantsTable Kr = new(invokeEffectTargetCountFactor1: 1f, invokeEffectTargetCountFactor2: 0.5f,
        invokeEffectTargetCountFactor3: 0.33f, invokeEffectTargetCountFactor4: 0.25f, invokeEffectTargetCountFactor5: 0.2f,
        invokeEffectTargetCountFactor6: 0.167f, invokeEffectTargetCountFactor7: 0.143f, invokeEffectTargetCountFactor8: 0.125f,
        invokeEffectTargetCountFactor9: 0.111f, invokeEffectTargetCountFactor10: 0.1f);

    private static BeginCondition Condition(float probability) {
        return new BeginCondition(0, 0, Gender.All, [], probability, 0, 0, 0, false, false, false, false, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);
    }

    [Test]
    public void Factor_ByTheTargetsHit() {
        Assert.Multiple(() => {
            Assert.That(SkillCastRules.TargetCountFactor(Kr, 1), Is.EqualTo(1f), "one target");
            Assert.That(SkillCastRules.TargetCountFactor(Kr, 3), Is.EqualTo(0.33f), "three targets");
            Assert.That(SkillCastRules.TargetCountFactor(Kr, 8), Is.EqualTo(0.125f), "8, the targetCount of 10700001");
            Assert.That(SkillCastRules.TargetCountFactor(Kr, 12), Is.EqualTo(0.1f), "more than 10 take the factor of 10");
            Assert.That(SkillCastRules.TargetCountFactor(Kr, 0), Is.EqualTo(1f), "no target");
            Assert.That(SkillCastRules.TargetCountFactor(new ConstantsTable(), 8), Is.EqualTo(1f), "a table without the rows");
        });
    }

    [Test]
    public void Roll_IsTheProbabilityTimesTheFactor() {
        Assert.Multiple(() => {
            // 10700001 lv1 p0: probability 0.07; on 8 targets 0.00875 each.
            Assert.That(SkillCastRules.ProcHolds(0.07f, 0.125f, 0.008), Is.True);
            Assert.That(SkillCastRules.ProcHolds(0.07f, 0.125f, 0.01), Is.False);
            Assert.That(SkillCastRules.ProcHolds(0.07f, 1f, 0.01), Is.True, "without the factor");
            Assert.That(SkillCastRules.ProcHolds(1f, 1f, 0.999999), Is.True, "a sure proc");
        });
    }

    // A player's proc of probability 1 on 8 targets holds about 1 time in 8.
    [Test]
    public void Check_TakesTheFactor() {
        var test = new TestGameSession();
        test.Start();
        test.Session.Ride = new RideManager(test.Session);
        FieldPlayer player = test.Session.Player;
        BeginCondition condition = Condition(1f);
        int held = 0;
        for (int i = 0; i < 4000; i++) {
            if (condition.Check(player, player, player, probabilityFactor: SkillCastRules.TargetCountFactor(Kr, 8))) {
                held++;
            }
        }
        Assert.That(held, Is.InRange(380, 620));
        Assert.That(condition.Check(player, player, player), Is.True, "no factor");
    }

    // An on-damage proc counts only the targets the hit dealt damage to: of two targets one evades (0), so the
    // factor is the one of 1 target, not of 2.
    [Test]
    public void OnDamage_CountsOnlyTheDamagedTargets() {
        var first = new TestGameSession();
        first.Start();
        var second = new TestGameSession();
        second.Start();
        FieldPlayer hit = first.Session.Player;
        FieldPlayer evaded = second.Session.Player;
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<ObjectId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(evaded, hit.ObjectId + 1);
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(10700181, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 300,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        var record = new DamageRecord(skill, attack) { SkillId = 10700181 };
        var hitRecord = new DamageRecordTarget(hit);
        hitRecord.AddDamage(DamageType.Normal, 300);
        var evadedRecord = new DamageRecordTarget(evaded);
        evadedRecord.AddDamage(DamageType.Miss, 0);
        record.Targets[hit.ObjectId] = hitRecord;
        record.Targets[evaded.ObjectId] = evadedRecord;
        Assert.Multiple(() => {
            Assert.That(SkillCastRules.DealtDamage(record, hit), Is.True);
            Assert.That(SkillCastRules.DealtDamage(record, evaded), Is.False);
            Assert.That(SkillCastRules.DamagedCount(record, [hit, evaded]), Is.EqualTo(1));
            Assert.That(SkillCastRules.TargetCountFactor(Kr, SkillCastRules.DamagedCount(record, [hit, evaded])), Is.EqualTo(1f));
        });
    }

    // 10700001 lv1 p0's conditionSkill, as the pack gives it.
    private const string PackRow = "<beginCondition level=\"1\" gender=\"2\" target=\"0\" probability=\"0.07\" invokeEffectFactor=\"1\" "
        + "cooldownTime=\"0\" useTargetCountFactor=\"{0}\" allowBattleRidingState=\"0\" onlyBattleRidingState=\"0\"><stat /></beginCondition>";

    private static BeginCondition Ingest(int flag) {
        var serializer = new XmlSerializer(typeof(Maple2.File.Parser.Xml.Skill.BeginCondition), new XmlRootAttribute("beginCondition"));
        var row = (Maple2.File.Parser.Xml.Skill.BeginCondition) serializer.Deserialize(new StringReader(string.Format(PackRow, flag)))!;
        return row.Convert();
    }

    [Test]
    public void Ingest_ReadsTheFlag() {
        Assert.Multiple(() => {
            Assert.That(Ingest(1).UseTargetCountFactor, Is.True, "pack row");
            Assert.That(Ingest(0).UseTargetCountFactor, Is.False, "flag off");
            var use = new PlayedClientActionAdditionals.Use(10700001, 1, 1, 1, true, 1, 0, false, 0.07f, UseTargetCountFactor: true);
            Assert.That(PlayedClientActionAdditionals.ToEffect(use).Condition!.Condition.UseTargetCountFactor, Is.True, "played row");
        });
    }
}

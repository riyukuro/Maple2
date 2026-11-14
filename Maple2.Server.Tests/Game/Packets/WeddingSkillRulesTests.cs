using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game.Packets;

public class WeddingSkillRulesTests {
    // The rows of weddingexp.xml and the item row of weddingskill.xml.
    private static readonly Dictionary<int, long> Grades = new() {
        [1] = 0, [2] = 1300, [3] = 2600, [4] = 4000, [5] = 5400, [6] = 7000,
    };

    private static WeddingTable Table() => new(
        new Dictionary<MarriageExpType, WeddingReward>(),
        new Dictionary<int, WeddingPackage>(),
        new Dictionary<int, WeddingSkill> {
            [1] = new WeddingSkill(1, WeddingSkillType.Passive, 1, 0, 0, 0),
            [5] = new WeddingSkill(5, WeddingSkillType.Item, 5, 20303183, 4, 1),
        },
        Grades);

    [Test]
    public void GradeIsTheOneBeforeTheFirstHigherRow() {
        Assert.That(WeddingSkillRules.Grade(0, Grades), Is.EqualTo(1));
        Assert.That(WeddingSkillRules.Grade(1299, Grades), Is.EqualTo(1));
        Assert.That(WeddingSkillRules.Grade(1300, Grades), Is.EqualTo(2));
        Assert.That(WeddingSkillRules.Grade(5400, Grades), Is.EqualTo(5));
        Assert.That(WeddingSkillRules.Grade(7000, Grades), Is.EqualTo(6));
        Assert.That(WeddingSkillRules.Grade(99999, Grades), Is.EqualTo(6));
        Assert.That(WeddingSkillRules.Grade(-1, Grades), Is.EqualTo(0));
        Assert.That(WeddingSkillRules.Grade(100, null), Is.EqualTo(0));
    }

    [Test]
    public void ItemSkillNeedsItsGrade() {
        Assert.That(WeddingSkillRules.ItemToGive(5, MaritalStatus.Married, 5399, Table(), _ => false), Is.Null);
        WeddingSkill? skill = WeddingSkillRules.ItemToGive(5, MaritalStatus.Married, 5400, Table(), _ => false);
        Assert.That(skill?.ItemId, Is.EqualTo(20303183));
        Assert.That(skill?.ItemRarity, Is.EqualTo(4));
        Assert.That(skill?.ItemAmount, Is.EqualTo(1));
    }

    [Test]
    public void RefusedCases() {
        // Held item, not married, passive or unknown skill, metadata from before the new ingest.
        Assert.That(WeddingSkillRules.ItemToGive(5, MaritalStatus.Married, 9000, Table(), id => id == 20303183), Is.Null);
        foreach (MaritalStatus status in new[] { MaritalStatus.Single, MaritalStatus.Engaged, MaritalStatus.DivorceFiledByGroom, MaritalStatus.DivorceFiledByBride, MaritalStatus.Divorced }) {
            Assert.That(WeddingSkillRules.ItemToGive(5, status, 9000, Table(), _ => false), Is.Null);
        }
        Assert.That(WeddingSkillRules.ItemToGive(1, MaritalStatus.Married, 9000, Table(), _ => false), Is.Null);
        Assert.That(WeddingSkillRules.ItemToGive(9, MaritalStatus.Married, 9000, Table(), _ => false), Is.Null);
        var old = new WeddingTable(new Dictionary<MarriageExpType, WeddingReward>(), new Dictionary<int, WeddingPackage>());
        Assert.That(WeddingSkillRules.ItemToGive(5, MaritalStatus.Married, 9000, old, _ => false), Is.Null);
    }

    // The passive rows of weddingskill.xml (requireGrade 1-4, maxCount 4/3/2/1) and the levels their effects have
    // (maxCount + 1 each).
    private static readonly Dictionary<int, short> EffectLevels = new() {
        [100000056] = 5, [100000057] = 4, [100000058] = 3, [100000059] = 2,
    };

    private static WeddingTable PassiveTable() => new(
        new Dictionary<MarriageExpType, WeddingReward>(),
        new Dictionary<int, WeddingPackage>(),
        new Dictionary<int, WeddingSkill> {
            [4] = new WeddingSkill(4, WeddingSkillType.Passive, 4, 0, 0, 0, AdditionalEffectId: 100000059),
            [1] = new WeddingSkill(1, WeddingSkillType.Passive, 1, 0, 0, 0, AdditionalEffectId: 100000056),
            [2] = new WeddingSkill(2, WeddingSkillType.Passive, 2, 0, 0, 0, AdditionalEffectId: 100000057),
            [3] = new WeddingSkill(3, WeddingSkillType.Passive, 3, 0, 0, 0, AdditionalEffectId: 100000058),
            [5] = new WeddingSkill(5, WeddingSkillType.Item, 5, 20303183, 4, 1, AdditionalEffectId: 100000099),
            [6] = new WeddingSkill(6, WeddingSkillType.Passive, 1, 0, 0, 0),
        },
        Grades);

    private static bool HasLevel(int id, short level) => EffectLevels.TryGetValue(id, out short max) && level >= 1 && level <= max;

    [Test]
    public void PassiveLevelsFollowTheGrade() {
        Assert.That(WeddingSkillRules.PassiveEffects(MaritalStatus.Married, 0, PassiveTable(), HasLevel),
            Is.EqualTo(new List<(int, short)> { (100000056, 1) }));
        Assert.That(WeddingSkillRules.PassiveEffects(MaritalStatus.Married, 2600, PassiveTable(), HasLevel),
            Is.EqualTo(new List<(int, short)> { (100000056, 3), (100000057, 2), (100000058, 1) }));
        // Grade 6: skill 1 would be level 6, its effect stops at 5 (maxCount 4 + 1); the others reach their cap.
        Assert.That(WeddingSkillRules.PassiveEffects(MaritalStatus.Married, 7000, PassiveTable(), HasLevel),
            Is.EqualTo(new List<(int, short)> { (100000056, 5), (100000057, 4), (100000058, 3), (100000059, 2) }));
    }

    [Test]
    public void PassiveNeedsACountingMarriage() {
        foreach (MaritalStatus status in new[] { MaritalStatus.DivorceFiledByGroom, MaritalStatus.DivorceFiledByBride }) {
            Assert.That(WeddingSkillRules.PassiveEffects(status, 4000, PassiveTable(), HasLevel), Has.Count.EqualTo(4));
        }
        foreach (MaritalStatus status in new[] { MaritalStatus.Single, MaritalStatus.Engaged, MaritalStatus.Divorced }) {
            Assert.That(WeddingSkillRules.PassiveEffects(status, 7000, PassiveTable(), HasLevel), Is.Empty);
        }
        // An effect missing from the skill metadata, and metadata from before the new ingest.
        Assert.That(WeddingSkillRules.PassiveEffects(MaritalStatus.Married, 7000, PassiveTable(), (_, _) => false), Is.Empty);
        Assert.That(WeddingSkillRules.PassiveEffects(MaritalStatus.Married, 7000, Table(), HasLevel), Is.Empty);
        var old = new WeddingTable(new Dictionary<MarriageExpType, WeddingReward>(), new Dictionary<int, WeddingPackage>());
        Assert.That(WeddingSkillRules.PassiveEffects(MaritalStatus.Married, 7000, old, HasLevel), Is.Empty);
    }
}

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
}

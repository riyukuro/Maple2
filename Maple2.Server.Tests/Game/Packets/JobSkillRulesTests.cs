using System;
using System.Collections.Generic;
using System.Linq;
﻿using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game.Packets;

// Job 9 and 11: the skill tree the client sends is checked as SkillPointSimulator::Validate checks it, and
// taken whole or not at all.
public class JobSkillRulesTests {
    private const int Limit1 = 80;
    private const int Limit2 = 70;
    private static readonly int[] NoExtra = [0, 0];

    private static readonly JobSkillState BaseSkill = new(1, SkillRank.Basic, 1, 10, 1);
    private static readonly JobSkillState Skill = new(2, SkillRank.Basic, 0, 10, 0);
    private static readonly JobSkillState Awakened = new(3, SkillRank.Awakening, 0, 10, 0);

    private static SkillMetadataUpgrade? NoUpgrade(int id, short level) => null;
    private static bool NoQuest(int questId) => false;

    private static JobSkillError Check(IReadOnlyCollection<JobSkillState> tree, short level, int[]? extra = null,
            Func<int, short, SkillMetadataUpgrade?>? upgrade = null, Func<int, bool>? quest = null, int limit1 = Limit1, int limit2 = Limit2) {
        return JobSkillRules.Check(tree, level, limit1, limit2, extra ?? NoExtra, upgrade ?? NoUpgrade, quest ?? NoQuest);
    }

    [Test]
    public void LevelPoints() {
        Assert.That(JobSkillRules.LevelPoints(9, SkillRank.Basic, Limit1, Limit2), Is.EqualTo(0));
        Assert.That(JobSkillRules.LevelPoints(10, SkillRank.Basic, Limit1, Limit2), Is.EqualTo(1));
        Assert.That(JobSkillRules.LevelPoints(50, SkillRank.Basic, Limit1, Limit2), Is.EqualTo(41));
        Assert.That(JobSkillRules.LevelPoints(90, SkillRank.Basic, Limit1, Limit2), Is.EqualTo(71));
        Assert.That(JobSkillRules.LevelPoints(59, SkillRank.Awakening, Limit1, Limit2), Is.EqualTo(0));
        Assert.That(JobSkillRules.LevelPoints(60, SkillRank.Awakening, Limit1, Limit2), Is.EqualTo(1));
        Assert.That(JobSkillRules.LevelPoints(90, SkillRank.Awakening, Limit1, Limit2), Is.EqualTo(11));
    }

    [Test]
    public void UsedPoints() {
        Assert.That(JobSkillRules.UsedPoints(0, 0), Is.EqualTo(0));
        Assert.That(JobSkillRules.UsedPoints(3, 0), Is.EqualTo(3));
        Assert.That(JobSkillRules.UsedPoints(1, 1), Is.EqualTo(0));
        Assert.That(JobSkillRules.UsedPoints(4, 1), Is.EqualTo(3));
    }

    [Test]
    public void ApplySentLevels() {
        List<JobSkillState> tree = JobSkillRules.Apply([BaseSkill, Skill, Awakened with { Level = 2 }],
            [(1, 3, true), (2, 5, false), (99, 4, true)]);
        Assert.That(tree.Select(skill => skill.Level), Is.EqualTo(new short[] { 3, 0, 2 }));

        // A base skill sent as not learned stays at its base level.
        tree = JobSkillRules.Apply([BaseSkill with { Level = 4 }], [(1, 0, false)]);
        Assert.That(tree[0].Level, Is.EqualTo(1));
    }

    [Test]
    public void PointsPerRank() {
        // Level 14: 5 base points, no awakening points.
        Assert.That(Check([BaseSkill with { Level = 3 }, Skill with { Level = 3 }], 14), Is.EqualTo(JobSkillError.None));
        Assert.That(Check([BaseSkill with { Level = 3 }, Skill with { Level = 4 }], 14), Is.EqualTo(JobSkillError.Points));
        Assert.That(Check([Awakened with { Level = 1 }], 14), Is.EqualTo(JobSkillError.Points));

        // Extra points count for their own rank only.
        Assert.That(Check([Awakened with { Level = 1 }], 14, [0, 1]), Is.EqualTo(JobSkillError.None));
        Assert.That(Check([Skill with { Level = 6 }], 14, [0, 1]), Is.EqualTo(JobSkillError.Points));
        Assert.That(Check([Skill with { Level = 6 }], 14, [1, 0]), Is.EqualTo(JobSkillError.None));
    }

    [Test]
    public void NoLimitsNoPointCheck() {
        Assert.That(Check([Skill with { Level = 10 }], 1, limit1: 0, limit2: 0), Is.EqualTo(JobSkillError.None));
    }

    [Test]
    public void MaxLevel() {
        Assert.That(Check([Skill with { Level = 11 }], 90), Is.EqualTo(JobSkillError.MaxLevel));
    }

    [Test]
    public void Requirements() {
        var need = new SkillMetadataUpgrade(20, [1], [2], [500]);
        SkillMetadataUpgrade? Upgrade(int id, short level) => id == 2 && level == 3 ? need : null;

        JobSkillState[] tree = [BaseSkill with { Level = 2 }, Skill with { Level = 3 }];
        Assert.That(Check(tree, 30, upgrade: Upgrade, quest: id => id == 500), Is.EqualTo(JobSkillError.None));
        Assert.That(Check(tree, 19, upgrade: Upgrade, quest: id => id == 500), Is.EqualTo(JobSkillError.CharacterLevel));
        Assert.That(Check(tree, 30, upgrade: Upgrade), Is.EqualTo(JobSkillError.RequireQuest));
        Assert.That(Check([BaseSkill, Skill with { Level = 3 }], 30, upgrade: Upgrade, quest: id => id == 500),
            Is.EqualTo(JobSkillError.RequireSkill));

        // A required skill that is not in the tree, or not learned, fails; a skill that is not learned asks nothing.
        var other = new SkillMetadataUpgrade(0, [7], [0], []);
        Assert.That(Check([Skill with { Level = 1 }], 30, upgrade: (_, _) => other), Is.EqualTo(JobSkillError.RequireSkill));
        Assert.That(Check([Skill], 30, upgrade: (_, _) => other), Is.EqualTo(JobSkillError.None));
    }

    [Test]
    public void RequirementOnTheSentTree() {
        // The required skill is raised in the same request.
        var need = new SkillMetadataUpgrade(0, [1], [3], []);
        List<JobSkillState> tree = JobSkillRules.Apply([BaseSkill, Skill], [(1, 3, true), (2, 1, true)]);
        Assert.That(Check(tree, 30, upgrade: (id, _) => id == 2 ? need : null), Is.EqualTo(JobSkillError.None));
    }
}

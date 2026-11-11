using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game.Util;

public class AchievementDebugTests {
    private static Achievement ThreeGrades() {
        var grades = new Dictionary<int, AchievementMetadataGrade>();
        foreach ((int grade, long value) in new[] { (1, 10L), (2, 50L), (3, 100L) }) {
            grades[grade] = new AchievementMetadataGrade(grade, new ConditionMetadata(ConditionType.map, value, null, null), null);
        }
        return new Achievement(new AchievementMetadata(1, null, false, AchievementCategory.Combat, [], grades));
    }

    // Achieve 5 button 1 (UIAchieveDialog onDbgDown): one grade back, the reward grade stays.
    [Test]
    public void DownKeepsTheRewardGrade() {
        Achievement achievement = ThreeGrades();
        achievement.Counter = 60;
        achievement.Grades[1] = 1;
        achievement.Grades[2] = 2;
        achievement.CurrentGrade = 3;
        achievement.RewardGrade = 3; // the rewards of grades 1 and 2 were given

        Assert.That(AchievementManager.DebugGradeBack(achievement), Is.True);
        Assert.That(achievement.Counter, Is.EqualTo(10));
        Assert.That(achievement.Grades.Keys, Is.EquivalentTo(new[] { 1 }));
        Assert.That(achievement.CurrentGrade, Is.EqualTo(2));
        Assert.That(achievement.RewardGrade, Is.EqualTo(3));
        Assert.That(AchievementManager.RewardDue(achievement), Is.False);

        // Grade 2 finished again: its reward was given, and grade 3's is not due before grade 3 is finished.
        achievement.Grades[2] = 3;
        achievement.CurrentGrade = 3;
        Assert.That(AchievementManager.RewardDue(achievement), Is.False);
        achievement.Grades[3] = 4;
        Assert.That(AchievementManager.RewardDue(achievement), Is.True);

        Assert.That(AchievementManager.DebugGradeBack(achievement), Is.True);
        Assert.That(AchievementManager.DebugGradeBack(achievement), Is.True);
        Assert.That(AchievementManager.DebugGradeBack(achievement), Is.True);
        Assert.That(achievement.Counter, Is.EqualTo(0));
        Assert.That(achievement.CurrentGrade, Is.EqualTo(1));
        Assert.That(AchievementManager.DebugGradeBack(achievement), Is.False);
    }

    // The normal path: a grade just finished whose reward is not given yet.
    [Test]
    public void RewardIsDueForAFinishedGrade() {
        Achievement achievement = ThreeGrades();
        achievement.CurrentGrade = 1;
        achievement.RewardGrade = 1;
        Assert.That(AchievementManager.RewardDue(achievement), Is.False);
        achievement.Grades[1] = 1;
        achievement.CurrentGrade = 2;
        Assert.That(AchievementManager.RewardDue(achievement), Is.True);
    }

    // A gear score at or below the kept score sends nothing (a stats refresh on each buff re-reported it).
    [Test]
    public void GearScoreAdvancesOnlyWhenHigher() {
        Assert.That(AchievementManager.Advances(ConditionType.item_gear_score, 400, 400), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.item_gear_score, 400, 350), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.item_gear_score, 400, 401), Is.True);
        Assert.That(AchievementManager.Advances(ConditionType.map, 400, 1), Is.True);
    }
}

using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game.Util;

// The titles and emotes of account trophies, which every character of the account takes once the account has them.
public class AchievementAccountRewardTests {
    // KR 21100065 (account): skill condition, grades 1 and 2 without a reward, grade 3 the title 10000651.
    private static Achievement Kr21100065(bool accountWide = true) {
        int[] codes = [10100261, 10100271, 10100281, 10100291, 10100204, 10100294];
        var grades = new Dictionary<int, AchievementMetadataGrade>();
        foreach ((int grade, long value) in new[] { (1, 10L), (2, 100L), (3, 1000L) }) {
            var condition = new ConditionMetadata(ConditionType.skill, value, new ConditionMetadata.Parameters(Integers: codes), null);
            AchievementMetadataReward? reward = grade == 3 ? new AchievementMetadataReward(AchievementRewardType.title, 10000651, 0, 1) : null;
            grades[grade] = new AchievementMetadataGrade(grade, condition, reward);
        }
        return new Achievement(new AchievementMetadata(21100065, null, accountWide, AchievementCategory.Combat, [], grades));
    }

    // KR 22210017 (account): one grade, the emote 90200043.
    private static Achievement Kr22210017() {
        var condition = new ConditionMetadata(ConditionType.quest_revise_achieve, 8,
            new ConditionMetadata.Parameters(Integers: [90000660, 90000661, 90000670, 90000680, 90000690, 90000692, 90000760, 90000762]), null);
        var grades = new Dictionary<int, AchievementMetadataGrade> {
            [1] = new(1, condition, new AchievementMetadataReward(AchievementRewardType.dynamicaction, 90200043, 0, 1)),
        };
        return new Achievement(new AchievementMetadata(22210017, null, true, AchievementCategory.Adventure, [], grades));
    }

    [Test]
    public void ClaimedTitleIsHeldByTheAccount() {
        Achievement achievement = Kr21100065();
        achievement.Grades[1] = 1;
        achievement.Grades[2] = 2;
        achievement.Grades[3] = 3;
        achievement.CurrentGrade = 3;
        achievement.RewardGrade = 4; // grade 3's title claimed

        AchievementMetadataReward[] rewards = AchievementManager.AccountRewardsHeld(achievement).ToArray();
        Assert.That(rewards.Select(reward => reward.Code), Is.EqualTo(new[] { 10000651 }));
        Assert.That(rewards[0].Type, Is.EqualTo(AchievementRewardType.title));
    }

    [Test]
    public void UnclaimedTitleIsNotHeld() {
        Achievement achievement = Kr21100065();
        achievement.Grades[1] = 1;
        achievement.Grades[2] = 2;
        achievement.Grades[3] = 3;
        achievement.CurrentGrade = 3;
        achievement.RewardGrade = 3; // finished, waiting for the claim
        Assert.That(AchievementManager.AccountRewardsHeld(achievement), Is.Empty);
    }

    // The GM debug "down" button takes grade 3 back and keeps the reward grade: no title for the others then.
    [Test]
    public void GradeTakenBackGivesNothing() {
        Achievement achievement = Kr21100065();
        achievement.Grades[1] = 1;
        achievement.Grades[2] = 2;
        achievement.Grades[3] = 3;
        achievement.CurrentGrade = 3;
        achievement.RewardGrade = 4;
        Assert.That(AchievementManager.DebugGradeBack(achievement), Is.True);
        Assert.That(AchievementManager.AccountRewardsHeld(achievement), Is.Empty);
    }

    [Test]
    public void CharacterTrophyGivesNothing() {
        Achievement achievement = Kr21100065(accountWide: false);
        achievement.Grades[1] = 1;
        achievement.Grades[2] = 2;
        achievement.Grades[3] = 3;
        achievement.CurrentGrade = 3;
        achievement.RewardGrade = 4;
        Assert.That(AchievementManager.AccountRewardsHeld(achievement), Is.Empty);
    }

    // The emote is paid on the grade without a claim; the reward grade moves past it at once.
    [Test]
    public void ReachedEmoteIsHeldByTheAccount() {
        Achievement achievement = Kr22210017();
        achievement.Grades[1] = 1;
        achievement.CurrentGrade = 1;
        achievement.RewardGrade = 2;
        AchievementMetadataReward[] rewards = AchievementManager.AccountRewardsHeld(achievement).ToArray();
        Assert.That(rewards.Select(reward => (reward.Type, reward.Code)),
            Is.EqualTo(new[] { (AchievementRewardType.dynamicaction, 90200043) }));
    }

    // The account's items are not given again.
    [Test]
    public void ClaimedItemIsNotGivenAgain() {
        var condition = new ConditionMetadata(ConditionType.skill, 1, null, null);
        var grades = new Dictionary<int, AchievementMetadataGrade> {
            [1] = new(1, condition, new AchievementMetadataReward(AchievementRewardType.item, 20000001, 1, 1)),
        };
        var achievement = new Achievement(new AchievementMetadata(23000001, null, true, AchievementCategory.Life, [], grades));
        achievement.Grades[1] = 1;
        achievement.CurrentGrade = 1;
        achievement.RewardGrade = 2;
        Assert.That(AchievementManager.AccountRewardsHeld(achievement), Is.Empty);
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class TrophyReportRulesTests {
    // KMS2: a 100 meret purchase moved use_merat from 88412 to 88512; a gain sends no update.
    [Test]
    public void MeretSpentIsTheAmountTakenOff() {
        Assert.That(TrophyReportRules.MeretSpent(-100), Is.EqualTo(100));
        Assert.That(TrophyReportRules.MeretSpent(-1200), Is.EqualTo(1200));
        Assert.That(TrophyReportRules.MeretSpent(0), Is.EqualTo(0));
        Assert.That(TrophyReportRules.MeretSpent(1100), Is.EqualTo(0));
    }

    [Test]
    public void MeretSpentAddsToTheCounter() {
        Assert.That(AchievementManager.Advances(ConditionType.use_merat, 88412, 100), Is.True);
        Assert.That(AchievementManager.Advances(ConditionType.home_goto, 16, 1), Is.True);
    }

    // The guide's go-home step sent while standing in the own home is no move home.
    [Test]
    public void GoHomeCountsOnlyFromElsewhere() {
        Assert.That(TrophyReportRules.CountsAsGoHome(alreadyInOwnHome: false), Is.True);
        Assert.That(TrophyReportRules.CountsAsGoHome(alreadyInOwnHome: true), Is.False);
    }

    // KMS2: a check-in that left the home at level 3 with 50 exp moved interior_exp_offset to 450, and one that
    // raised the level from 1 to 3 moved interior_level from 1 to 2.
    [Test]
    public void InteriorTrophiesCountTheCheckIn() {
        Assert.That(TrophyReportRules.InteriorExpCounted(450, homeGainedExp: true), Is.EqualTo(450));
        Assert.That(TrophyReportRules.InteriorExpCounted(450, homeGainedExp: false), Is.EqualTo(0));
        Assert.That(TrophyReportRules.InteriorExpCounted(0, homeGainedExp: true), Is.EqualTo(0));
        Assert.That(TrophyReportRules.InteriorLevelRaised(1, 3), Is.True);
        Assert.That(TrophyReportRules.InteriorLevelRaised(3, 4), Is.True);
        Assert.That(TrophyReportRules.InteriorLevelRaised(3, 3), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.interior_exp_offset, 900, 450), Is.True);
        Assert.That(AchievementManager.Advances(ConditionType.interior_level, 1, 1), Is.True);
    }

    // hero_achieve_grade: a grade gained by a trophy inside the code range (21240001) or on the code list (21100019)
    // counts; any other trophy's grade does not.
    [TestCase(21220032, true)]
    [TestCase(21230046, true)]
    [TestCase(21210009, false)]
    [TestCase(21240001, false)]
    public void HeroAchieveGradeCountsTrophiesInTheCodeRange(int trophyId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.hero_achieve_grade, 10,
            new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(21220001, 21230999)), null);
        Assert.That(condition.Check(null!, codeLong: trophyId), Is.EqualTo(expected));
    }

    [TestCase(21100010, true)]
    [TestCase(21100049, true)]
    [TestCase(21100019, false)]
    public void HeroAchieveGradeCountsTrophiesOnTheCodeList(int trophyId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.hero_achieve_grade, 1,
            new ConditionMetadata.Parameters(Integers: [21100010, 21100011, 21100049]), null);
        Assert.That(condition.Check(null!, codeLong: trophyId), Is.EqualTo(expected));
    }

    // quest_revise_achieve: only the quests the trophy lists count (22210017 lists 8 guide quests).
    [TestCase(90000660, true)]
    [TestCase(90000762, true)]
    [TestCase(90000661 + 1, false)]
    public void QuestReviseAchieveCountsTheListedQuests(int questId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.quest_revise_achieve, 8,
            new ConditionMetadata.Parameters(Integers: [90000660, 90000661, 90000670, 90000680, 90000690, 90000692, 90000760, 90000762]), null);
        Assert.That(condition.Check(null!, codeLong: questId), Is.EqualTo(expected));
    }

    // KMS2 sends dungeon_first_clear only with the record update that shows 1 clear.
    [Test]
    public void FirstClearOnlyAtOneClear() {
        Assert.That(TrophyReportRules.IsFirstClear(DungeonGroupType.normal, 1), Is.True);
        Assert.That(TrophyReportRules.IsFirstClear(DungeonGroupType.raid, 1), Is.True);
        Assert.That(TrophyReportRules.IsFirstClear(DungeonGroupType.normal, 2), Is.False);
        Assert.That(TrophyReportRules.IsFirstClear(DungeonGroupType.normal, 0), Is.False);
    }

    // The colosseum record's TotalClears is the round reached, not a clear count.
    [Test]
    public void ColosseumRoundIsNotAFirstClear() {
        Assert.That(TrophyReportRules.IsFirstClear(DungeonGroupType.colosseum, 1), Is.False);
    }

    // A trophy row without the locking attribute stays countable.
    [Test]
    public void TrophyIsUnlockedByDefault() {
        var metadata = new AchievementMetadata(1, null, false, AchievementCategory.Life, [], new System.Collections.Generic.Dictionary<int, AchievementMetadataGrade>());
        Assert.That(metadata.Locked, Is.False);
        Assert.That((metadata with { Locked = true }).Locked, Is.True);
        // AchievementManager.Update asks this before a trophy takes any progress.
        Assert.That(TrophyReportRules.Counts(metadata), Is.True);
        Assert.That(TrophyReportRules.Counts(metadata with { Locked = true }), Is.False);
    }
}

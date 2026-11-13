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

    // KMS2: a check-in that left the home at level 3 with 50 exp moved interior_exp_offset to 450. interior_level
    // takes the home's level and keeps the highest: KMS2 trophies end at the home's level (2 at level 2, 4 at level 4).
    [Test]
    public void InteriorTrophiesCountTheCheckIn() {
        Assert.That(TrophyReportRules.InteriorExpCounted(450, homeGainedExp: true), Is.EqualTo(450));
        Assert.That(TrophyReportRules.InteriorExpCounted(450, homeGainedExp: false), Is.EqualTo(0));
        Assert.That(TrophyReportRules.InteriorExpCounted(0, homeGainedExp: true), Is.EqualTo(0));
        Assert.That(TrophyReportRules.InteriorLevelCount(3), Is.EqualTo(3));
        Assert.That(TrophyReportRules.InteriorLevelCount(-1), Is.EqualTo(0));
        Assert.That(AchievementManager.Advances(ConditionType.interior_exp_offset, 900, 450), Is.True);
        // A level 1 home that skipped to 3, or an older home at 7 whose trophy holds 1, catches up in one report.
        Assert.That(AchievementManager.Advances(ConditionType.interior_level, 1, 3), Is.True);
        Assert.That(AchievementManager.Advances(ConditionType.interior_level, 1, 7), Is.True);
        // A check-in that did not raise the level reports nothing.
        Assert.That(AchievementManager.Advances(ConditionType.interior_level, 3, 3), Is.False);
    }

    // maid_affinity reports the closeness level a maid reached (23500014 "level {0}", values 2 to 10): a second maid
    // reaching a lower level does not move the trophy back or forward.
    [Test]
    public void MaidAffinityKeepsTheHighestLevel() {
        Assert.That(AchievementManager.KeepsHighest(ConditionType.maid_affinity), Is.True);
        Assert.That(AchievementManager.Advances(ConditionType.maid_affinity, 4, 5), Is.True);
        Assert.That(AchievementManager.Advances(ConditionType.maid_affinity, 5, 2), Is.False);
        Assert.That(AchievementManager.KeepsHighest(ConditionType.maid_profile), Is.False);
    }

    // A home is created when its name is empty. An empty name would create it again (decoration level back to 1),
    // so it is refused; and the creation report of level 1 counts only when the trophy holds nothing yet.
    [Test]
    public void HomeCreationCountsInteriorLevelOnce() {
        Assert.That(TrophyReportRules.HomeNameAllowed("", 16), Is.False);
        Assert.That(TrophyReportRules.HomeNameAllowed("  ", 16), Is.False);
        Assert.That(TrophyReportRules.HomeNameAllowed("Home", 16), Is.True);
        Assert.That(TrophyReportRules.HomeNameAllowed(new string('a', 17), 16), Is.False);
        long created = TrophyReportRules.InteriorLevelCount(1);
        Assert.That(AchievementManager.Advances(ConditionType.interior_level, 0, created), Is.True);
        Assert.That(AchievementManager.Advances(ConditionType.interior_level, 1, created), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.interior_level, 4, created), Is.False);
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

    // The GM trophy command names a locked trophy and a trophy that got no row instead of a silent success.
    [Test]
    public void GmTrophyCommandSaysWhyNothingChanged() {
        (bool ok, string line) = TrophyReportRules.GmTrophyOutcome(21100058, locked: true, hasRow: false, 0, 0);
        Assert.That(ok, Is.False);
        Assert.That(line, Does.Contain("21100058").And.Contain("locked"));

        (ok, line) = TrophyReportRules.GmTrophyOutcome(7, locked: false, hasRow: false, 0, 0);
        Assert.That(ok, Is.False);
        Assert.That(line, Does.Contain("no progress"));

        (ok, line) = TrophyReportRules.GmTrophyOutcome(7, locked: false, hasRow: true, 3, 50);
        Assert.That(ok, Is.True);
        Assert.That(line, Does.Contain("grade 3").And.Contain("counter 50"));
    }
}

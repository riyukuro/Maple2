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
    }
}

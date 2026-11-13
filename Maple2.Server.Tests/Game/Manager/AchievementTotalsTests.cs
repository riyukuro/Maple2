using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The trophy totals as the client tallies them (CAchieveManager::Tally, achievemanager.cpp:725): only a trophy of
// type 2 (the id's ten-millions digit, achievedata.cpp:910) counts, each cleared grade once, in the total of its
// category (the id's millions digit, :911). Other trophies, such as the 9xxxxxxx ones, count nothing.
public class AchievementTotalsTests {
    private const int Combat = 21100001;
    private const int Life = 23100099;
    private const int Other = 91100001; // type 9, category digit 1
    private const int Map = 2000062;

    private TestGameSession test = null!;
    private GameSession session = null!;

    private static AchievementMetadata Trophy(int id, AchievementCategory category, int type) =>
        TestGameSession.Trophy(id, ConditionType.home_goto, [1, 2, 3], category: category) with { Type = type };

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.AddAchievement(Trophy(Combat, AchievementCategory.Combat, 2), Trophy(Life, AchievementCategory.Life, 2),
            Trophy(Other, AchievementCategory.Combat, 9));
        test.Start();
        session = test.Session;
        test.EnterMap(TestGameSession.Map(Map));
        test.TakeSent();
    }

    [Test]
    public void ClearedGradesOfTypeTwoCount() {
        session.ConditionUpdate(ConditionType.home_goto);
        session.ConditionUpdate(ConditionType.home_goto);

        AchievementInfo info = session.Player.Value.Character.AchievementInfo;
        Assert.That((info.Combat, info.Adventure, info.Lifestyle), Is.EqualTo((2, 0, 2)));
        Assert.That(test.Errors, Is.Empty);
    }

    // A stored row counts by its trophy's metadata, not the category it was stored with: rows written before the
    // ingest gave the client's category kept the categoryTag's, and the type 9 ones counted as life.
    [Test]
    public void StoredRowsCountByTheirMetadata() {
        using (GameStorage.Request db = test.Storage.Context()) {
            Store(db, Combat, AchievementCategory.Life, 2);
            Store(db, Life, AchievementCategory.Combat, 3);
            Store(db, Other, AchievementCategory.Life, 1);
        }

        using GameStorage.Request check = test.Storage.Context();
        AchievementInfo info = check.GetAchievementInfo(session.AccountId, session.CharacterId);
        Assert.That((info.Combat, info.Adventure, info.Lifestyle), Is.EqualTo((2, 0, 3)));
    }

    private void Store(GameStorage.Request db, int id, AchievementCategory storedCategory, int grades) {
        test.Session.AchievementMetadata.TryGet(id, out AchievementMetadata? metadata);
        var achievement = new Achievement(metadata!) { Category = storedCategory, CurrentGrade = grades };
        for (int grade = 1; grade <= grades; grade++) {
            achievement.Grades[grade] = 1;
        }
        Assert.That(db.CreateAchievement(session.CharacterId, achievement), Is.Not.Null);
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// require achievement/achievementGrade and unreqAchievement (CQuestManagerCommon::CanAcceptQuest,
// questdata.cpp:538-551): grade 0 needs the trophy completed (questmanager_2.cpp:398: state 3), a grade needs that
// grade cleared (CAchieveManager::IsGradeCleared, achievemanager.cpp:418). A trophy that is only started is not
// enough. An unrequired trophy refuses the quest once its grade is cleared.
public class QuestAchievementRequireTests {
    private const int Main = 10001004;
    private const int Trophy = 22100001;
    private const int Map = 2000062;

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        // Three grades: one, two and three home visits.
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [1, 2, 3]));
        test.Start();
        session = test.Session;
        test.EnterMap(TestGameSession.Map(Map));
        test.TakeSent();
    }

    private void Clear(int grades) {
        for (int i = 0; i < grades; i++) {
            session.ConditionUpdate(ConditionType.home_goto);
        }
    }

    private bool CanStart(QuestMetadataRequire require) {
        QuestMetadata quest = TestGameSession.QuestData(Main) with { Require = require };
        test.Register(quest);
        return session.Quest.CanStart(quest);
    }

    private static QuestMetadataRequire Require() => TestGameSession.QuestData(Main).Require;

    [TestCase(0, 0, false)] // no record
    [TestCase(0, 1, false)]
    [TestCase(1, 0, false)] // started but not completed
    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    [TestCase(2, 2, true)]
    [TestCase(3, 0, true)]  // completed
    [TestCase(3, 3, true)]
    public void Achievement(int cleared, int grade, bool canStart) {
        Clear(cleared);
        Assert.That(CanStart(Require() with { Achievement = Trophy, AchievementGrade = grade }), Is.EqualTo(canStart));
        Assert.That(test.Errors, Is.Empty);
    }

    [TestCase(0, 2, true)]
    [TestCase(1, 2, true)]
    [TestCase(2, 2, false)]
    [TestCase(3, 3, false)]
    [TestCase(3, 0, true)] // no grade: the client checks nothing
    public void UnrequiredAchievement(int cleared, int grade, bool canStart) {
        Clear(cleared);
        Assert.That(CanStart(Require() with { UnrequiredAchievement = (Trophy, grade) }), Is.EqualTo(canStart));
    }
}

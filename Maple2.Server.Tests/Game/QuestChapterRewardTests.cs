using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// Completing a chapter's end quest (QuestManager.Complete): when the chapter's begin quest is completed the player
// gets the chapter book's stat and skill points. Made-up quest ids.
public class QuestChapterRewardTests {
    private const int Begin = 80000001, End = 80000002;

    private TestGameSession test = null!;

    private GameSession Login(QuestState beginState, int statPoints) {
        test = new TestGameSession();
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry> {
            [1] = new(1, Begin, End, [new ChapterBookTable.Entry.SkillPoint(2, 0), new ChapterBookTable.Entry.SkillPoint(1, 1)], statPoints, []),
        }));
        GameSession session = test.Start();
        ProgressionOrderTests.LoadConfig(test, session);
        typeof(ConfigManager).GetField("skillPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, new SkillPoint());
        test.StartQuest(TestGameSession.QuestData(Begin)).State = beginState;
        test.StartQuest(TestGameSession.QuestData(End));
        test.TakeSent();
        return session;
    }

    // The end quest closes the chapter: the book's skill points of each rank and its stat points are given.
    [Test]
    public void EndQuest_OfACompletedBeginGivesTheBooksPoints() {
        GameSession session = Login(QuestState.Completed, 3);
        session.Quest.TryGetQuest(End, out Quest? end);

        Assert.That(session.Quest.Complete(end!), Is.True);

        Assert.Multiple(() => {
            Assert.That(session.Config.ExtraSkillPoints(), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(session.Config.StatPoints(AttributePointSource.Quest), Is.EqualTo(3));
        });
    }

    // A chapter whose begin quest is not completed gives nothing, and a book without stat points gives none.
    [TestCase(QuestState.Started, 3, 0, 0)]
    [TestCase(QuestState.Completed, 0, 2, 0)]
    public void EndQuest_GivesNothingForAnOpenBeginOrNoStatPoints(QuestState begin, int stat, int baseSkill, int expectedStat) {
        GameSession session = Login(begin, stat);
        session.Quest.TryGetQuest(End, out Quest? end);

        Assert.That(session.Quest.Complete(end!), Is.True);

        Assert.Multiple(() => {
            Assert.That(session.Config.ExtraSkillPoints()[0], Is.EqualTo(baseSkill));
            Assert.That(session.Config.StatPoints(AttributePointSource.Quest), Is.EqualTo(expectedStat));
        });
    }
}

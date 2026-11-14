using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// Quest 16, the Maple guide window's move: the server moves the player for a row the window can offer
// (QuestRules.GuideOffered) and no other. The rows go home, so the move shows as the world's move request.
// Chapter 7 holds quests 70001-70003; row 1 is the chapter's first row. The ids are made up.
public class QuestGuideTests {
    private const int Chapter = 7;
    private const int First = 70001, Second = 70002, Third = 70003;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private static QuestMetadata Quest(int id, bool startable) {
        QuestMetadata quest = TestGameSession.QuestData(id);
        return quest with {
            Basic = quest.Basic with { ChapterId = Chapter },
            Require = quest.Require with { Level = startable ? (short) 0 : (short) 99 },
        };
    }

    // started / completed: the held quests by id; startable: the quests the player may start.
    private GameSession Start(Dictionary<int, QuestState> held, params int[] startable) {
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(90000100));
        session.Player.Value.Home.Indoor.Name = "home";
        session.QuestMetadata.GetQuests();
        foreach (int id in new[] { First, Second, Third }) {
            QuestMetadata quest = Quest(id, System.Array.IndexOf(startable, id) >= 0);
            test.Register(quest);
            if (held.TryGetValue(id, out QuestState state)) {
                test.StartQuest(quest).State = state;
            }
        }
        test.SetTable(new LearningQuestTable(new Dictionary<int, LearningQuestTable.Entry> {
            [1] = new(0, 0, First, 0, Constant.DefaultHomeMapId, 0),
            [2] = new(0, 99, Second, 0, Constant.DefaultHomeMapId, 0),
            [3] = new(0, 0, Third, 0, Constant.DefaultHomeMapId, 0),
        }));
        test.TakeSent();
        return session;
    }

    private bool Moves(GameSession session, int row) {
        var handler = new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(16);
            writer.WriteInt(row);
        }));
        return test.World.Moves.Count > 0;
    }

    // The guide quest is rewarded and the chapter goes on: the window still offers its row (old code refused it).
    [Test]
    public void RewardedGuideQuest_OfAnOpenChapterMoves() {
        GameSession session = Start(new() { [First] = QuestState.Completed, [Second] = QuestState.Started });
        Assert.That(Moves(session, 1), Is.True);
    }

    // The window checks no level for the move (old code refused a row above the player's level).
    [Test]
    public void RowAboveThePlayersLevel_Moves() {
        GameSession session = Start(new() { [Second] = QuestState.Started });
        session.Player.Value.Character.Level = 1;
        Assert.That(Moves(session, 2), Is.True);
    }

    // A row the window never shows: its quest not held nor startable, and not the chapter's first row (old code moved).
    [Test]
    public void RowTheWindowCannotShow_DoesNotMove() {
        GameSession session = Start(new() { [Second] = QuestState.Started });
        Assert.That(Moves(session, 3), Is.False);
    }

    // The chapter's first row is the tree's: it moves while the player holds a chapter quest.
    [Test]
    public void FirstRow_OfAChapterThePlayerIsIn_Moves() {
        GameSession session = Start(new() { [Second] = QuestState.Started });
        Assert.That(Moves(session, 1), Is.True);
    }

    // A startable guide quest's row moves, without any quest of the chapter held.
    [Test]
    public void StartableGuideQuest_Moves() {
        GameSession session = Start(new(), Third);
        Assert.That(Moves(session, 3), Is.True);
    }

    // A complete chapter offers none of its rows.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void CompleteChapter_DoesNotMove(int row) {
        GameSession session = Start(new() {
            [First] = QuestState.Completed, [Second] = QuestState.Completed, [Third] = QuestState.Completed,
        }, First, Second, Third);
        Assert.That(Moves(session, row), Is.False);
    }

    // An unknown row, or a row whose quest has no data, does nothing.
    [Test]
    public void UnknownRow_DoesNotMove() {
        GameSession session = Start(new() { [Second] = QuestState.Started });
        Assert.That(Moves(session, 4), Is.False);
    }
}

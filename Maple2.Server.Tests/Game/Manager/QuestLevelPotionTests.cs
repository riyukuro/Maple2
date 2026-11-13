using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A level potion completes the epic quests below its level. When one of them is a quest that opens the alliance
// missions (the pick methods' reloadOnComplete; KR 91000015 and 50101050), the fame board's lists (Quest 0x1F/0x20,
// questmanager_2.cpp:1145) are made again, as a completion by the quest's NPC does.
public class QuestLevelPotionTests {
    private const int Opener = 50101050;
    private const int Other = 50101040;

    private TestGameSession test = null!;
    private GameSession session = null!;

    private void Login(int reloadOnComplete) {
        test = new TestGameSession();
        var method = new FameTable.PickMethod(false, 2, [0, 1], 0, reloadOnComplete,
            [new FameTable.Alliance(AllianceType.KritiasMapleAlliance, 0)]);
        test.SetTable(new FameTable([method], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.Start();
        session = test.Session;
        session.QuestMetadata.GetQuests(); // loaded first: the first load returns only the stored quests
        test.Register(TestGameSession.QuestData(Opener));
        test.Register(TestGameSession.QuestData(Other));
        // Held as started: the test storage gives no row back for a quest the potion would create.
        test.StartQuest(TestGameSession.QuestData(Opener));
        test.TakeSent();
    }

    private int DailyLists() {
        byte[] head = QuestPacket.DailyAllianceMissions(false, []).ToArray()[..4];
        return test.TakeSent().Count(sent => TestGameSession.Op(sent) == SendOp.Quest && sent.AsSpan(0, 4).SequenceEqual(head));
    }

    [Test]
    public void CompletingTheOpenerMakesTheListsAgain() {
        Login(Opener);
        session.Quest.LevelPotion(69);
        Assert.That(session.Quest.TryGetQuest(Opener, out var quest) ? quest.State : QuestState.None, Is.EqualTo(QuestState.Completed));
        Assert.That(DailyLists(), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void OtherQuestsDoNot() {
        Login(91000015);
        session.Quest.LevelPotion(69);
        Assert.That(DailyLists(), Is.EqualTo(0));
    }
}

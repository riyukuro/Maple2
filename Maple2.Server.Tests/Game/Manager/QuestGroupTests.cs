using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// require groupID and unrequireGroup (CQuestManagerCommon::vfunc_o10_21, questdata.cpp:333, and CanAcceptQuest,
// questdata.cpp:520): only the group's pick of the period may be started, and a quest that names the group in
// unrequireGroup waits while that pick is in progress or done this period.
public class QuestGroupTests {
    private const int GroupId = 7300;
    private const int Unrequiring = 7400001;
    private static readonly int[] Members = Enumerable.Range(0, 18).Select(i => 7300001 + i).ToArray();

    private TestGameSession test = null!;
    private GameSession session = null!;

    private static QuestMetadata Daily(int id, int groupId = 0, int[]? unrequireGroup = null) {
        QuestMetadata quest = TestGameSession.QuestData(id, QuestType.EventQuest);
        return quest with {
            Basic = quest.Basic with { Repeat = 2 },
            Require = quest.Require with { GroupId = groupId, UnrequireGroup = unrequireGroup },
        };
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
        foreach (int id in Members) {
            test.Register(Daily(id, GroupId));
        }
        test.Register(Daily(Unrequiring, unrequireGroup: [GroupId]));
        // The in-memory metadata has no quest table, so the group index is set as GetGroup builds it.
        typeof(QuestMetadataStorage).GetField("groups", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(session.QuestMetadata, new Dictionary<int, int[]> { [GroupId] = Members });
    }

    private int Picked() => QuestGroupPick.Pick(Members, 2, GroupId, DateTime.Now).Single();

    private void Hold(int id, QuestState state, DateTime start) {
        session.QuestMetadata.TryGet(id, out QuestMetadata? metadata);
        Quest quest = test.StartQuest(metadata!);
        quest.State = state;
        quest.StartTime = new DateTimeOffset(start).ToUnixTimeSeconds();
    }

    // The stored quests are already the client's by feature (the ingest's filter), so without a played client every
    // grouped quest is a member, in id order; with one, a quest it lacks is not.
    [TestCase(false, new[] { 7300001, 7300002, 7300003 })]
    [TestCase(true, new[] { 7300001, 7300003 })]
    public void Groups_WithAndWithoutPlayedClient(bool marked, int[] expected) {
        QuestMetadata unknown = Daily(7300002, GroupId);
        unknown = unknown with { Basic = unknown.Basic with { NotInPlayedClient = marked } };
        Dictionary<int, int[]> groups = QuestMetadataStorage.Groups([Daily(7300003, GroupId), unknown, Daily(7300001, GroupId), Daily(7400001)]);
        Assert.That(groups.Keys, Is.EqualTo(new[] { GroupId }));
        Assert.That(groups[GroupId], Is.EqualTo(expected));
    }

    [Test]
    public void GroupPicks_IsTheClientPick() {
        Assert.That(session.Quest.GroupPicks(GroupId, DateTime.Now), Is.EqualTo(new[] { Picked() }));
        Assert.That(session.Quest.GroupPicks(GroupId + 1, DateTime.Now), Is.Empty);
    }

    [Test]
    public void CanStart_OnlyThePick() {
        int picked = Picked();
        Assert.Multiple(() => {
            foreach (int id in Members) {
                session.QuestMetadata.TryGet(id, out QuestMetadata? metadata);
                Assert.That(session.Quest.CanStart(metadata!), Is.EqualTo(id == picked), $"{id}");
            }
        });
    }

    [Test]
    public void Unrequire_FreeWhenThePickIsNotHeld() {
        session.QuestMetadata.TryGet(Unrequiring, out QuestMetadata? metadata);
        Assert.That(session.Quest.CanStart(metadata!), Is.True);
    }

    [Test]
    public void Unrequire_RefusedWhileThePickIsInProgress() {
        Hold(Picked(), QuestState.Started, DateTime.Now);
        session.QuestMetadata.TryGet(Unrequiring, out QuestMetadata? metadata);
        Assert.That(session.Quest.CanStart(metadata!), Is.False);
    }

    [Test]
    public void Unrequire_RefusedWhenThePickIsDoneToday() {
        Hold(Picked(), QuestState.Completed, DateTime.Now);
        session.QuestMetadata.TryGet(Unrequiring, out QuestMetadata? metadata);
        Assert.That(session.Quest.CanStart(metadata!), Is.False);
    }

    // Done on an earlier day, the daily pick may be taken again, so it no longer holds the quest back.
    [Test]
    public void Unrequire_FreeWhenThePickWasDoneBefore() {
        Hold(Picked(), QuestState.Completed, DateTime.Now.AddDays(-2));
        session.QuestMetadata.TryGet(Unrequiring, out QuestMetadata? metadata);
        Assert.That(session.Quest.CanStart(metadata!), Is.True);
    }

    // Another quest of the group in progress (a pick of an earlier day) does not count.
    [Test]
    public void Unrequire_OtherMemberDoesNotCount() {
        Hold(Members.First(id => id != Picked()), QuestState.Started, DateTime.Now);
        session.QuestMetadata.TryGet(Unrequiring, out QuestMetadata? metadata);
        Assert.That(session.Quest.CanStart(metadata!), Is.True);
    }
}

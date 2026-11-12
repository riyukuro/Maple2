using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Quest 0x18 (the claim button of the daily mission page and the level guide, uidailymissiondialog.cpp:321) and the
// level count at a quest's start. Made-up quest ids and levels.
public class QuestRemoteClaimTests {
    private static Quest Held(QuestState state, params ConditionMetadata[] conditions) {
        var basic = new QuestMetadataBasic(ChapterId: 0, Type: QuestType.DailyMission, Account: 0, StandardLevel: 1,
            Forfeitable: false, EventTag: string.Empty, AutoStart: true, Disabled: false, UsePostbox: false, StartNpc: 0,
            CompleteNpc: 0, CompleteMaps: null, ProgressMaps: null, Repeat: 0, ResetWeekday: 0, DurationMinutes: 0,
            Alliance: AllianceType.AnyFaction);
        var require = new QuestMetadataRequire(0, 0, [], [], [], 0, (0, 0), 0, 0);
        var metadata = new QuestMetadata(900001, null, basic, require, null!, null!, null!, null!, null!, null!,
            null, null, null, QuestEventMissionType.none, conditions);
        var quest = new Quest(metadata) { State = state };
        for (int i = 0; i < conditions.Length; i++) {
            quest.Conditions.Add(i, new Quest.Condition(conditions[i]));
        }
        return quest;
    }

    private static ConditionMetadata Level(int target, long value = 1, ConditionMetadata.Parameters? codes = null, int partyCount = 0) =>
        new(ConditionType.level, value, codes, new ConditionMetadata.Parameters(Integers: [target]), partyCount);

    [Test]
    public void Claim_NotHeld_SaysWhetherTheServerKnowsTheQuest() {
        Assert.Multiple(() => {
            Assert.That(QuestRules.RemoteClaimCheck(false, null, false, false), Is.EqualTo(QuestRules.RemoteClaim.NoMetadata));
            Assert.That(QuestRules.RemoteClaimCheck(true, null, false, false), Is.EqualTo(QuestRules.RemoteClaim.NotHeld));
        });
    }

    [TestCase(QuestState.Started, true, true, QuestRules.RemoteClaim.Done)]
    [TestCase(QuestState.Started, true, false, QuestRules.RemoteClaim.Unmet)]
    [TestCase(QuestState.Started, false, true, QuestRules.RemoteClaim.NotInClient)]
    [TestCase(QuestState.Completed, true, true, QuestRules.RemoteClaim.Completed)]
    public void Claim_Held(QuestState state, bool known, bool met, QuestRules.RemoteClaim expected) {
        Assert.That(QuestRules.RemoteClaimCheck(true, Held(state), known, met), Is.EqualTo(expected));
    }

    [TestCase(QuestRules.RemoteClaim.Done, false)]
    [TestCase(QuestRules.RemoteClaim.InventoryFull, false)] // the inventory error is the answer
    [TestCase(QuestRules.RemoteClaim.Completed, false)] // a second click before the Complete packet
    [TestCase(QuestRules.RemoteClaim.NoMetadata, true)]
    [TestCase(QuestRules.RemoteClaim.NotHeld, true)]
    [TestCase(QuestRules.RemoteClaim.NotInClient, true)]
    [TestCase(QuestRules.RemoteClaim.Unmet, true)]
    public void Refusal_Answer(QuestRules.RemoteClaim claim, bool answered) {
        Assert.That(QuestRules.AnswersRefusal(claim), Is.EqualTo(answered));
    }

    [TestCase(12, 1)] // above the target
    [TestCase(10, 1)] // at the target
    [TestCase(9, 0)] // below: the level-up counts it later
    public void LevelAtStart(int level, int expected) {
        Quest quest = Held(QuestState.Started, Level(10));
        QuestRules.CountLevelAtStart(quest.Conditions.Values, level);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(expected));
    }

    [Test]
    public void LevelAtStart_LeavesOtherConditions() {
        Quest quest = Held(QuestState.Started,
            new ConditionMetadata(ConditionType.level_up, 1, null, new ConditionMetadata.Parameters(Integers: [5])),
            Level(5, codes: new ConditionMetadata.Parameters(Integers: [1])),
            Level(5, partyCount: 2),
            Level(5, value: 3));
        bool changed = QuestRules.CountLevelAtStart(quest.Conditions.Values, 20);
        Assert.Multiple(() => {
            Assert.That(changed, Is.True);
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0)); // level_up: only an actual level-up
            Assert.That(quest.Conditions[1].Counter, Is.EqualTo(0)); // with codes
            Assert.That(quest.Conditions[2].Counter, Is.EqualTo(0)); // party count
            Assert.That(quest.Conditions[3].Counter, Is.EqualTo(1)); // one step, as one level-up
        });
    }

    [Test]
    public void LevelAtStart_FullCounterUnchanged() {
        Quest quest = Held(QuestState.Started, Level(5));
        quest.Conditions[0].Counter = 1;
        Assert.That(QuestRules.CountLevelAtStart(quest.Conditions.Values, 20), Is.False);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
}

using System;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Client sender: client/ms2/questmanager_2.cpp:2206 (count, then one quest id each); the ids come from the
// quest table's self-starting list and the list of the current field (commonfiles/questdata.cpp:1951).
public class QuestRulesTests {
    [Test]
    public void ClientBytes_OnlySelfStartingIdsPass() {
        var writer = new ByteWriter();
        writer.WriteInt(3);
        writer.WriteInt(1);
        writer.WriteInt(2);
        writer.WriteInt(3);
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray(), 0);

        // 1: self-starting world quest, 2: exploration quest of field 2000001, 3: a quest given by an NPC.
        (QuestType Type, bool Auto, bool Locked, int[]? Maps)[] table = [
            (QuestType.WorldQuest, true, false, null),
            (QuestType.FieldMission, false, false, [2000001, 2000002]),
            (QuestType.EpicQuest, false, false, null),
        ];
        int count = reader.ReadInt();
        var passed = new System.Collections.Generic.List<int>();
        for (int i = 0; i < count; i++) {
            int id = reader.ReadInt();
            (QuestType type, bool auto, bool locked, int[]? maps) = table[id - 1];
            if (QuestRules.ClientStartsItself(type, auto, locked, maps, 2000001)) {
                passed.Add(id);
            }
        }

        Assert.That(reader.Position, Is.EqualTo(16));
        Assert.That(passed, Is.EqualTo(new[] { 1, 2 }));
    }

    [TestCase(QuestType.EpicQuest, true, true, false)] // locked
    [TestCase(QuestType.EpicQuest, false, false, false)] // given by an NPC
    [TestCase(QuestType.EpicQuest, true, false, true)]
    [TestCase(QuestType.FieldMission, true, false, false)] // exploration quests go by field only
    public void ClientStartsItself_OtherTypes(QuestType type, bool autoStart, bool locked, bool expected) {
        Assert.That(QuestRules.ClientStartsItself(type, autoStart, locked, null, 2000001), Is.EqualTo(expected));
    }

    // Quest sub-type 19: the gate of CQuestCommon::FUN_140744670 (questdata.cpp:221) and the alive check.
    [TestCase(QuestState.Started, true, 2000002, 2000001, false, true)]
    [TestCase(QuestState.Completed, true, 2000002, 2000001, false, false)] // not in progress
    [TestCase(QuestState.Started, false, 2000002, 2000001, false, false)] // no useReEntryHiddenMap
    [TestCase(QuestState.Started, true, 0, 2000001, false, false)] // first progress map not set
    [TestCase(QuestState.Started, true, 2000001, 2000001, false, false)] // already there
    [TestCase(QuestState.Started, true, 2000002, 2000001, true, false)] // dead
    public void CanReEnterHiddenMap(QuestState state, bool flag, int firstMap, int mapId, bool isDead, bool expected) {
        Assert.That(QuestRules.CanReEnterHiddenMap(state, flag, [firstMap, 2000009], mapId, isDead), Is.EqualTo(expected));
    }

    [Test]
    public void CanReEnterHiddenMap_NoProgressMaps() {
        Assert.That(QuestRules.CanReEnterHiddenMap(QuestState.Started, true, null, 2000001, false), Is.False);
        Assert.That(QuestRules.CanReEnterHiddenMap(QuestState.Started, true, [], 2000001, false), Is.False);
    }

    // Quest sub-type 28: the tests of CQuestConditionCheckerUserFind (conditionutil.cpp:882). Made-up ids.
    private static readonly QuestRules.FoundUser Found = new(JobCode: 30, Level: 42, Gender: 1, FinderGender: 0, TitleId: 777, RideId: 555, EquipItemIds: [1001, 1002]);

    [TestCase("dict:{jobId:30}", true)]
    [TestCase("dict:{jobId:40}", false)]
    [TestCase("dict:{level:40 100}", true)]
    [TestCase("dict:{level:43 100}", false)]
    [TestCase("dict:{level:40}", false)] // the client refuses a level that is not a pair
    [TestCase("dict:{gender:1}", true)]
    [TestCase("dict:{gender:0}", false)]
    [TestCase("dict:{gender:2}", true)] // 2 is either
    [TestCase("dict:{equipItemIds:1001 1002}", true)]
    [TestCase("dict:{equipItemIds:1001 1003}", false)]
    [TestCase("dict:{equipItemIdsAnd:1001 1003}", false)]
    [TestCase("dict:{equipItemIdsOr:1003 1002}", true)]
    [TestCase("dict:{equipItemIdsOr:1003 1004}", false)]
    [TestCase("dict:{titleId:777}", true)]
    [TestCase("dict:{titleId:778}", false)]
    [TestCase("dict:{rideeId:555}", true)]
    [TestCase("dict:{rideeId:556}", false)]
    [TestCase("dict:{oppositeGender:1}", true)]
    [TestCase("dict:{oppositeGender:0}", false)]
    [TestCase("dict:{somethingElse:5}", true)] // a key without a test is skipped
    [TestCase("titleId:777", false)] // not a dictionary
    public void MatchesUserFind_OneKey(string target, bool expected) {
        Assert.That(QuestRules.MatchesUserFind([target], Found), Is.EqualTo(expected));
    }

    [Test]
    public void MatchesUserFind_SplitDictionaryNeedsEveryKey() {
        // The ingest splits the target on commas, so a dictionary of three keys arrives as three pieces.
        Assert.That(QuestRules.MatchesUserFind(["dict:{level:30 100", "gender:1", "jobId:30}"], Found), Is.True);
        Assert.That(QuestRules.MatchesUserFind(["dict:{level:30 100", "gender:0", "jobId:30}"], Found), Is.False);
        Assert.That(QuestRules.MatchesUserFind(null, Found), Is.False);
        Assert.That(QuestRules.MatchesUserFind([], Found), Is.False);
        Assert.That(QuestRules.ParseDict(["dict:{level:30 100", "gender:1}"]), Is.EqualTo(new System.Collections.Generic.Dictionary<string, string> { ["level"] = "30 100", ["gender"] = "1" }));
    }

    [TestCase(false, false, false, 200f, 155f, 150f, true)]
    [TestCase(false, false, false, 306f, 155f, 150f, false)] // too far
    [TestCase(false, false, false, 9000f, 0f, 150f, true)] // constant not loaded
    [TestCase(true, false, false, 10f, 155f, 150f, false)] // PvP field
    [TestCase(false, true, false, 0f, 155f, 150f, false)] // the player's own id
    [TestCase(false, false, true, 10f, 155f, 150f, false)] // dead target
    public void CanFindUser(bool pvpField, bool self, bool targetDead, float distance, float reach, float slack, bool expected) {
        Assert.That(QuestRules.CanFindUser(pvpField, self, targetDead, distance, reach, slack), Is.EqualTo(expected));
    }

    [Test]
    public void ClientStartsItself_ExplorationQuestOfAnotherField() {
        Assert.That(QuestRules.ClientStartsItself(QuestType.FieldMission, false, false, [2000002, 2000001], 2000001), Is.False);
        Assert.That(QuestRules.ClientStartsItself(QuestType.FieldMission, false, false, [], 2000001), Is.False);
    }
}

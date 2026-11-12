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

    // Client rule: CQuestManagerCommon::vfunc_o10_21 (commonfiles/questdata.cpp:376) and NextWeekdayStart
    // (timeutil.cpp:403). 2026-11-11 is a Wednesday (weekday 4).
    private static long Local(int year, int month, int day, int hour) =>
        new DateTimeOffset(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Local)).ToUnixTimeSeconds();

    [TestCase(0, 0, 2026, 11, 20, false)] // done once
    [TestCase(2, 0, 2026, 11, 11, false)] // daily, same day
    [TestCase(2, 0, 2026, 11, 12, true)] // daily, next day
    [TestCase(3, 2, 2026, 11, 15, false)] // weekly reset monday: sunday before it
    [TestCase(3, 2, 2026, 11, 16, true)] // weekly reset monday: from monday
    [TestCase(3, 4, 2026, 11, 17, false)] // weekly reset wednesday, started on a wednesday: a week later
    [TestCase(3, 4, 2026, 11, 18, true)]
    [TestCase(1, 0, 2026, 11, 11, true)] // other repeat: at once
    public void CanStartAgain_ClientRule(int repeat, int resetWeekday, int year, int month, int day, bool expected) {
        long started = Local(2026, 11, 11, 23);
        var now = new DateTime(year, month, day, 0, 30, 0, DateTimeKind.Local);
        Assert.That(QuestRules.CanStartAgain(repeat, resetWeekday, started, now), Is.EqualTo(expected));
    }

    [TestCase("", 0)]
    [TestCase("sun", 1)]
    [TestCase("Mon", 2)]
    [TestCase("SAT", 7)]
    [TestCase("60", 0)] // a duration, not a day
    public void ResetWeekday_ClientNames(string usePeriod, int expected) {
        Assert.That(Maple2.Model.Metadata.QuestPeriod.ResetWeekday(usePeriod), Is.EqualTo(expected));
    }

    // Made-up fields: go-to field 100, complete npc in 200 and 201.
    [TestCase(true, 100, QuestState.Started, true, 300, true)]
    [TestCase(false, 100, QuestState.Started, true, 300, false)] // gotoNpc not enabled
    [TestCase(true, 0, QuestState.Started, true, 300, false)] // no gotoField
    [TestCase(true, 100, QuestState.Completed, true, 300, false)] // already rewarded
    [TestCase(true, 100, QuestState.Started, false, 300, false)] // a condition not met yet
    [TestCase(true, 100, QuestState.Started, true, 100, false)] // already in the gotoField
    [TestCase(true, 100, QuestState.Started, true, 201, false)] // in a field of the complete npc
    public void GoToNpc_ClientButtonRule(bool enabled, int gotoField, QuestState state, bool met, int mapId, bool expected) {
        var goTo = new Maple2.Model.Metadata.QuestMetadataGoToNpc(enabled, gotoField, 1);
        Assert.That(QuestRules.CanGoToNpc(goTo, state, met, [200, 201], mapId), Is.EqualTo(expected));
    }

    // The talk a quest's script opens with at an npc: the accept talk for a quest not held or one that can be
    // started again, the progress talk for a started quest (also at its complete npc while a condition is open),
    // the complete talk only with every condition met at the complete npc, none for a completed quest.
    [TestCase(null, false, false, false, 100)]
    [TestCase(QuestState.None, false, false, true, 100)]
    [TestCase(QuestState.Started, false, false, false, 200)]
    [TestCase(QuestState.Started, false, false, true, 200)]
    [TestCase(QuestState.Started, false, true, false, 200)]
    [TestCase(QuestState.Started, false, true, true, 300)]
    [TestCase(QuestState.Completed, false, true, true, 0)]
    [TestCase(QuestState.Completed, true, true, true, 100)]
    public void TalkStateBase(QuestState? held, bool canStartAgain, bool conditionsMet, bool atCompleteNpc, int expected) {
        Assert.That(QuestRules.TalkStateBase(held, canStartAgain, conditionsMet, atCompleteNpc), Is.EqualTo(expected));
    }

    // The complete request's npc: the quest's complete npc on the field (npc id 0: not on the field), or none for a
    // quest that is completed remotely or has no complete npc.
    // A remote completion that names a field: on that field only; at the npc from any.
    [TestCase(11000001, QuestRemoteType.None, true, 11000001, true)]
    [TestCase(11000001, QuestRemoteType.None, true, 11000002, false)]
    [TestCase(11000001, QuestRemoteType.None, true, 0, false)]
    [TestCase(11000001, QuestRemoteType.Popup, true, 11000001, true)]
    [TestCase(0, QuestRemoteType.None, true, 11000002, true)]
    [TestCase(11000001, QuestRemoteType.None, false, 0, false)]
    [TestCase(11000001, QuestRemoteType.Popup, false, 0, true)]
    [TestCase(0, QuestRemoteType.None, false, 0, true)]
    [TestCase(11000001, QuestRemoteType.Popup, false, 0, true, 2000023)]
    [TestCase(11000001, QuestRemoteType.Popup, false, 0, false, 2000001)]
    [TestCase(11000001, QuestRemoteType.Popup, true, 11000001, true, 2000001)]
    [TestCase(0, QuestRemoteType.None, false, 0, true, 2000001)]
    public void CanCompleteAt(int completeNpc, QuestRemoteType remote, bool named, int npcId, bool expected, int remoteMap = 0) {
        Maple2.Model.Metadata.QuestMetadata data = Maple2.Server.Tests.Game.TestGameSession.QuestData(90000001);
        data = data with {
            Basic = data.Basic with { CompleteNpc = completeNpc },
            RemoteComplete = new Maple2.Model.Metadata.QuestRemoteComplete(remote, remoteMap, false),
        };
        Assert.That(QuestRules.CanCompleteAt(data, named, npcId, 2000023), Is.EqualTo(expected));
    }

    // CQuestCommon::GetEndTime / IsExpired (commonfiles/questdata.cpp:241, 253): a duration ends minutes after the
    // start, a reset weekday at the start of that weekday after the start, an event tag at its term's end (0 when the
    // term is gone or the quest was started outside it), and both at the earlier one. A quest with none never ends.
    private static Maple2.Model.Metadata.QuestMetadataBasic Basic(int weekday = 0, int minutes = 0, string tag = "") =>
        TestGameSession.QuestData(90000001).Basic with { ResetWeekday = weekday, DurationMinutes = minutes, EventTag = tag };

    private const long Start = 1_800_000_000;

    [TestCase(0, false)]
    [TestCase(30 * 60 - 1, false)]
    [TestCase(30 * 60, true)]
    public void Duration_EndsMinutesAfterTheStart(long elapsed, bool expired) {
        Assert.That(QuestRules.EndTime(Basic(minutes: 30), Start, Start + elapsed, null), Is.EqualTo(Start + 30 * 60));
        Assert.That(QuestRules.IsExpired(Basic(minutes: 30), Start, Start + elapsed, null), Is.EqualTo(expired));
    }

    [Test]
    public void Weekday_EndsAtTheNextStartOfThatWeekday() {
        // A Monday (weekday 2) start ends at the next Monday's midnight, local time.
        var monday = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Local);
        long start = new DateTimeOffset(monday).ToUnixTimeSeconds();
        long end = new DateTimeOffset(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Local)).ToUnixTimeSeconds();
        Assert.That(QuestRules.EndTime(Basic(weekday: 2), start, start, null), Is.EqualTo(end));
        Assert.That(QuestRules.IsExpired(Basic(weekday: 2), start, end - 1, null), Is.False);
        Assert.That(QuestRules.IsExpired(Basic(weekday: 2), start, end, null), Is.True);
    }

    [Test]
    public void EventTag_EndsWithItsTerm() {
        (long, long) term = (Start - 100, Start + 1000);
        Assert.That(QuestRules.EndTime(Basic(tag: "t"), Start, Start, term), Is.EqualTo(Start + 1000));
        Assert.That(QuestRules.EndTime(Basic(tag: "t"), Start, Start, null), Is.EqualTo(0));
        Assert.That(QuestRules.EndTime(Basic(tag: "t"), Start - 200, Start, term), Is.EqualTo(0));
        Assert.That(QuestRules.EndTime(Basic(minutes: 10, tag: "t"), Start, Start, term), Is.EqualTo(Start + 600));
        Assert.That(QuestRules.IsExpired(Basic(tag: "t"), Start, Start, term), Is.False);
        Assert.That(QuestRules.IsExpired(Basic(tag: "t"), Start, Start, null), Is.True);
    }

    [Test]
    public void NoEndFields_NeverExpires() {
        Assert.That(QuestRules.IsExpired(Basic(), Start, long.MaxValue, null), Is.False);
    }
}

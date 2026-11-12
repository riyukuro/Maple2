using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.File.Ingest;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Table.Server;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The condition rows of the KR epic chain lv10-40, copied from the quest data. Each row goes through the ingest's
// ConvertCodes, as QuestMapper builds it, and is checked with the values its emitter sends.
public class EpicChainConditionTests {
    private static ConditionMetadata Row(ConditionType type, string? code, int value, string? target) =>
        new(type, value == 0 ? 1 : value,
            (code == null ? [] : code.Split(',')).ConvertCodes(),
            (target == null ? [] : target.Split(',')).ConvertCodes());

    // lv10-40: the trigger rows (quest, code, target) and the set_achievement of the map's trigger sends the same
    // code: TriggerContext.SetAchievement(box, "trigger", code) -> ConditionUpdate(trigger, codeString: code).
    private static readonly object[] TriggerRows = [
        new object?[] { 60100010, "AudienceWithEreb", null },
        new object?[] { 60100015, "jordyhello", null },
        new object?[] { 60100030, "jordysave", "52000119" },
        new object?[] { 60100060, "jordysave2", "52000119" },
        new object?[] { 60100075, "jordysave3", "52000119" },
        new object?[] { 60100095, "mafiabattle", null },
        new object?[] { 60100115, "windead", null },
        new object?[] { 60100125, "nelfmissing", null },
        new object?[] { 60100130, "ClearYomiprincess", null },
        new object?[] { 60100145, "eveguard", null },
        new object?[] { 60100160, "ClearPollutedgarden", null },
        new object?[] { 60100185, "markguard", null },
        new object?[] { 60100210, "maskbattle", null },
        new object?[] { 60100220, "lenonn", null },
        new object?[] { 60100235, "CityWarfareClear", null },
        new object?[] { 50001551, "henesysinvasion", null },
        new object?[] { 50001562, "foolishson", null },
        new object?[] { 50001563, "ManovichMobKill", null },
        new object?[] { 50001563, "ManovichRetire", null },
        new object?[] { 50001568, "MeetAnos", null },
        new object?[] { 50001581, "Studyindarkness", null },
        new object?[] { 50001604, "AsimovRetire", null },
        new object?[] { 50001605, "KatelyninGrief", null },
        new object?[] { 10003073, "Windvalley", null },
        new object?[] { 10003079, "Namid", null },
        new object?[] { 10003095, "Namid2", null },
        new object?[] { 10003096, "evagor", null },
        new object?[] { 10003102, "WindValleyBattle", null },
        new object?[] { 10003106, "NewChief", null },
        new object?[] { 50001623, "meetingAnn", null },
        new object?[] { 50001632, "AnosReturns", null },
        new object?[] { 50001641, "ProtectFinish", null },
        new object?[] { 50001642, "ClearAnosSOS", null },
        new object?[] { 50001675, "ClearPP", null },
    ];

    [TestCaseSource(nameof(TriggerRows))]
    public void Trigger_CountsTheSetAchievementCode(int questId, string code, string? target) {
        Assert.That(Enum.TryParse("trigger", out ConditionType type) && type == ConditionType.trigger, Is.True);
        ConditionMetadata condition = Row(ConditionType.trigger, code, 1, target);
        Assert.That(condition.Check(null!, codeString: code), Is.True, $"{questId} {code}");
        // The match is exact: another trigger's code, or the code in another case, does not count.
        Assert.That(condition.Check(null!, codeString: "ClearHideandSeek"), Is.False);
        Assert.That(condition.Check(null!, codeString: code.ToUpperInvariant()), Is.False);
    }

    // lv12, 60100045: item_move code 30000119 target 600. On 02000084 the liftables are item 30000119 and the
    // eight target boxes have liftableTarget 600, for example at <2550, 1350, 2850>. The box is keyed by its
    // block (Vector3B), and placing the carried cube sends that block.
    [Test]
    public void ItemMove_CountsTheLiftableDroppedInItsBox() {
        ConditionMetadata condition = Row(ConditionType.item_move, "30000119", 1, "600");
        Assert.That(condition.Check(null!, targetLong: 600, codeLong: 30000119), Is.True);
        Assert.That(condition.Check(null!, targetLong: 600, codeLong: 30000120), Is.False);

        var box = new LiftableTargetBox(new Vector3(2550, 1350, 2850), Vector3.Zero, false, 600);
        var boxes = new Dictionary<Vector3B, LiftableTargetBox> { [box.Position] = box };
        Assert.That(boxes.TryGetValue(new Vector3B(17, 9, 19), out LiftableTargetBox? found), Is.True);
        Assert.That(condition.Check(null!, targetLong: found!.LiftableTarget, codeLong: 30000119), Is.True);
    }

    // lv22, 50001557: emotion code hello value 3 target 02000076. The client sends skill 90200011's paramStr
    // "hello" (skilldata 902, mainType 14) as the aniKey; 90200011 is a default emote of every character.
    [TestCase(2000076, "hello", true)]
    [TestCase(2000001, "hello", false)]
    [TestCase(2000076, "bow", false)]
    public void Emotion_CountsHelloOnTheTargetMap(int mapId, string aniKey, bool expected) {
        Assert.That(Constant.DefaultEmotes, Does.Contain(90200011));
        ConditionMetadata condition = Row(ConditionType.emotion, "hello", 3, "02000076");
        Assert.That(condition.Value, Is.EqualTo(3));
        Assert.That(condition.Check(SessionOnMap(mapId), codeString: aniKey), Is.EqualTo(expected));
    }

    // lv26, 50001583: swimtime value 20 target 52000133, no code. FieldPlayer counts one per second of Swim or
    // SwimDash with the map as the target.
    [TestCase(52000133, true)]
    [TestCase(2000076, false)]
    public void Swimtime_CountsSecondsOnTheTargetMap(int mapId, bool expected) {
        ConditionMetadata condition = Row(ConditionType.swimtime, null, 20, "52000133");
        Assert.That(condition.Codes, Is.Null);
        Assert.That(condition.Value, Is.EqualTo(20));
        Assert.That(condition.Check(null!, targetLong: mapId), Is.EqualTo(expected));
    }

    // lv29, 50001601: dungeon_clear code 25004001. 02000331's seeker01 trigger calls DungeonClear at the end of
    // its escort; the room counts dungeon_clear with its dungeon id once, as its first result.
    [Test]
    public void DungeonClear_CountsTheRoomOnce() {
        ConditionMetadata condition = Row(ConditionType.dungeon_clear, "25004001", 1, null);
        Assert.That(condition.Check(null!, codeLong: 25004001), Is.True);
        Assert.That(condition.Check(null!, codeLong: 25004002), Is.False);
        Assert.That(DungeonFieldManager.TakesResult(DungeonState.None, DungeonGroupType.normal), Is.True);
        Assert.That(DungeonFieldManager.TakesResult(DungeonState.Clear, DungeonGroupType.normal), Is.False);
        // Every player of the room gets a result row, so the clear reaches each one: a record has every type.
        var record = new DungeonUserRecord(25004001, 1);
        Assert.That(record.AccumulationRecords.Count, Is.EqualTo(Enum.GetValues<DungeonAccumulationRecordType>().Length));
    }

    // lv29, 50001603: QuestScriptFunction_final moves the player to 52000136 portal 1 from scripts 104 (accept)
    // and 200 (in progress), function 1, as an end function. The client sends NpcTalk 2 (Continue) after the
    // accept and the progress buttons, and HandleContinue runs the end function.
    [Test]
    public void QuestScriptMove_MapsBothScriptsToTheInstance() {
        IEnumerable<(int, IDictionary<int, QuestScriptFunction>)> rows = [
            (50001603, new Dictionary<int, QuestScriptFunction> {
                [104] = new() { functionID = 1, endFunction = true, moveFieldID = 52000136, moveFieldPortalID = 1 },
                [200] = new() { functionID = 1, endFunction = true, moveFieldID = 52000136, moveFieldPortalID = 1 },
            }),
        ];
        var results = ServerTableMapper.MergeQuestScriptFunctions(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>(), rows);
        foreach (int scriptId in new[] { 104, 200 }) {
            ScriptFunctionMetadata function = results[50001603][scriptId][1];
            Assert.That(function.Type, Is.EqualTo(ScriptType.Quest));
            Assert.That(function.EndFunction, Is.True);
            Assert.That(function.MoveMapId, Is.EqualTo(52000136));
            Assert.That(function.MovePortalId, Is.EqualTo(1));
        }
    }

    // A session whose player stands on mapId. Nothing else of it is set up.
    private static GameSession SessionOnMap(int mapId) {
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(session);
        var player = (FieldPlayer) RuntimeHelpers.GetUninitializedObject(typeof(FieldPlayer));
        GC.SuppressFinalize(player);
        var value = new Player(new Account { Username = "a" }, new Character { Name = "A", Mastery = new Mastery(), MapId = mapId }, 1) {
            Currency = new Currency(),
            Unlock = new Unlock(),
            Home = (Home) RuntimeHelpers.GetUninitializedObject(typeof(Home)),
        };
        typeof(Actor<Player>).GetField("<Value>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
        typeof(GameSession).GetProperty(nameof(GameSession.Player))!.SetValue(session, player);
        return session;
    }
}

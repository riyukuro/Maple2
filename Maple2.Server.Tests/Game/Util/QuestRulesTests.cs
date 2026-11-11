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

    [Test]
    public void ClientStartsItself_ExplorationQuestOfAnotherField() {
        Assert.That(QuestRules.ClientStartsItself(QuestType.FieldMission, false, false, [2000002, 2000001], 2000001), Is.False);
        Assert.That(QuestRules.ClientStartsItself(QuestType.FieldMission, false, false, [], 2000001), Is.False);
    }
}

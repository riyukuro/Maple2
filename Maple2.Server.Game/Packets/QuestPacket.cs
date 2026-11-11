using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class QuestPacket {
    private enum Command : byte {
        Error = 0,
        Talk = 1,
        Start = 2,
        Update = 3,
        Complete = 4,
        Unknown5 = 5,
        Abandon = 6,
        Expired = 7,
        SetTracking = 9,
        SummonPortal = 18,
        ExplorationProgress = 21,
        LoadQuestStates = 22,
        LoadQuests = 23,
        Unknown25 = 25,
        ExplorationReward = 26,
        Unknown30 = 30,
        DailyReputationMissions = 31,
        WeeklyReputationMissions = 32,
        AllianceAccept = 34,
        AllianceComplete = 35,
        Unknown38 = 38,
    }

    public static ByteWriter Error(QuestError error) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteInt((int) error);

        return pWriter;
    }

    public static ByteWriter Talk(FieldNpc npc, ICollection<QuestMetadata> quests) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Talk);
        pWriter.WriteInt(npc.ObjectId);
        pWriter.WriteInt(quests.Count);
        foreach (QuestMetadata quest in quests) {
            pWriter.WriteInt(quest.Id);
        }

        return pWriter;
    }

    public static ByteWriter Start(Quest quest) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Start);
        pWriter.WriteInt(quest.Id);
        pWriter.WriteLong(quest.StartTime);
        pWriter.WriteBool(quest.Track);
        pWriter.WriteInt(quest.Conditions.Count);
        foreach (Quest.Condition condition in quest.Conditions.Values) {
            pWriter.WriteInt(condition.Counter);
        }

        return pWriter;
    }

    public static ByteWriter Update(Quest quest) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteInt(quest.Id);
        pWriter.WriteInt(quest.Conditions.Count);
        foreach (Quest.Condition condition in quest.Conditions.Values) {
            pWriter.WriteInt(condition.Counter);
        }

        return pWriter;
    }

    public static ByteWriter Complete(Quest quest) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Complete);
        pWriter.WriteInt(quest.Id);
        pWriter.WriteInt(quest.CompletionCount); // the same field the quest list record carries after the state
        pWriter.WriteLong(quest.EndTime);

        return pWriter;
    }

    public static ByteWriter Unknown5() {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Unknown5);
        pWriter.WriteInt();
        pWriter.WriteInt();

        return pWriter;
    }

    public static ByteWriter Abandon(int questId) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Abandon);
        pWriter.WriteInt(questId);

        return pWriter;
    }

    public static ByteWriter Expired(ICollection<int> questIds) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Expired);
        pWriter.WriteInt(questIds.Count);
        foreach (int questId in questIds) {
            pWriter.WriteInt(questId);
        }

        return pWriter;
    }

    public static ByteWriter SetTracking(int questId, bool tracked) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.SetTracking);
        pWriter.WriteInt(questId);
        pWriter.WriteBool(tracked);

        return pWriter;
    }

    public static ByteWriter SummonPortal(int npcObjectId, int portalId, int startTick) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.SummonPortal);
        pWriter.WriteInt(npcObjectId);
        pWriter.WriteInt(portalId);
        pWriter.WriteInt(startTick);

        return pWriter;
    }

    public static ByteWriter LoadExploration(int starAmount) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.ExplorationProgress);
        pWriter.WriteInt(starAmount);
        pWriter.WriteInt();

        return pWriter;
    }

    public static ByteWriter LoadQuestStates(ICollection<Quest> quests) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.LoadQuestStates);
        pWriter.WriteInt(quests.Count);
        foreach (Quest quest in quests) {
            pWriter.WriteClass<Quest>(quest);
        }

        return pWriter;
    }

    public static ByteWriter LoadQuests(ICollection<int> questIds) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.LoadQuests);
        pWriter.WriteInt(questIds.Count);
        foreach (int questId in questIds) {
            pWriter.WriteInt(questId);
        }

        return pWriter;
    }

    // CMentoringMission?
    public static ByteWriter Unknown25() {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Unknown25);
        pWriter.WriteLong();

        return pWriter;
    }

    public static ByteWriter UpdateExploration(int starAmount) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.ExplorationReward);
        pWriter.WriteInt(starAmount);

        return pWriter;
    }

    // CFieldQuest?
    public static ByteWriter Unknown30() {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Unknown30);

        return pWriter;
    }

    // Quest 0x1F, the day's alliance missions (CQuestManager, questmanager_2.cpp:1145): the client lets a daily alliance
    // quest be started only when it is listed. drop: the client first removes its held daily alliance quests.
    public static ByteWriter DailyAllianceMissions(bool drop, ICollection<int> questIds) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.DailyReputationMissions);
        pWriter.WriteBool(drop);
        pWriter.WriteInt(questIds.Count);
        foreach (int questId in questIds) {
            pWriter.WriteInt(questId);
        }

        return pWriter;
    }

    // Quest 0x20, the week's alliance missions (questmanager_2.cpp:1152): the same for the weekly ones without a group;
    // drop removes the held weekly alliance quests without a group.
    public static ByteWriter WeeklyAllianceMissions(bool drop, ICollection<int> questIds) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.WeeklyReputationMissions);
        pWriter.WriteBool(drop);
        pWriter.WriteInt(questIds.Count);
        foreach (int questId in questIds) {
            pWriter.WriteInt(questId);
        }

        return pWriter;
    }

    // Quest 0x22: the client closes the alliance quest window and shows s_quest_alliance_accept_all_{alliance}.
    public static ByteWriter AllianceAccept(AllianceType type) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.AllianceAccept);
        pWriter.WriteShort((short) type);

        return pWriter;
    }

    // Quest 0x23: the same with s_quest_alliance_complete_all_{alliance}.
    public static ByteWriter AllianceComplete(AllianceType type) {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.AllianceComplete);
        pWriter.WriteShort((short) type);

        return pWriter;
    }

    public static ByteWriter Unknown38() {
        var pWriter = Packet.Of(SendOp.Quest);
        pWriter.Write<Command>(Command.Unknown38);

        return pWriter;
    }
}

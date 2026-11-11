using System;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// Quest sub-type 18 (client cinematiccomponent.cpp:611: byte 0x12, int quest id). The answer is the quest
// portal packet the client reads in networkservice.cpp:677: byte 0x12, int NPC object id, int portal id, int tick.
public class QuestSummonPortalTests {
    [Test]
    public void Reply_IsMode18_NpcPortalTick() {
        ByteWriter writer = QuestPacket.SummonPortal(0x01020304, 9, 0x00001000);
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

        Assert.That(body, Is.EqualTo(new byte[] {
            0x12, // mode
            0x04, 0x03, 0x02, 0x01, // NPC object id
            0x09, 0x00, 0x00, 0x00, // portal id
            0x00, 0x10, 0x00, 0x00, // start tick
        }));
    }

    // CQuestManager::OnQuest mode 0 (questmanager_2.cpp:946): one int, the error. 7 is the alliance quest
    // completion ticket text, the answer to sub-type 33.
    [Test]
    public void Error_IsMode0_ThenTheErrorAsInt() {
        ByteWriter writer = QuestPacket.Error(Maple2.Model.Error.QuestError.s_alliance_quest_completion_ticket_error);
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

        Assert.That(body, Is.EqualTo(new byte[] {
            0x00, // mode
            0x07, 0x00, 0x00, 0x00, // error
        }));
    }

    // The answer to sub-type 34 (accept all alliance quests): error 3, s_quest_error_accept_fail.
    [Test]
    public void Error_AcceptFailIs3() {
        ByteWriter writer = QuestPacket.Error(Maple2.Model.Error.QuestError.s_quest_error_accept_fail);
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

        Assert.That(body, Is.EqualTo(new byte[] { 0x00, 0x03, 0x00, 0x00, 0x00 }));
    }

    [Test]
    public void OnlyAStartedQuestWithAPortal_InATalk_WithNoPortalUp() {
        Assert.That(QuestRules.CanResummonPortal(QuestState.Started, true, true, false), Is.True);
        Assert.That(QuestRules.CanResummonPortal(QuestState.Completed, true, true, false), Is.False);
        Assert.That(QuestRules.CanResummonPortal(QuestState.None, true, true, false), Is.False);
        Assert.That(QuestRules.CanResummonPortal(QuestState.Started, false, true, false), Is.False);
        Assert.That(QuestRules.CanResummonPortal(QuestState.Started, true, false, false), Is.False);
        Assert.That(QuestRules.CanResummonPortal(QuestState.Started, true, true, true), Is.False);
    }
}

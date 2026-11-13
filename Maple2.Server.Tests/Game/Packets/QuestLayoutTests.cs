using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The Quest packet against the client's reader, CQuestManager::OnQuest (questmanager_2.cpp:942): each writer's bytes
// in the order and widths the client reads them. Error, SummonPortal and the alliance modes have their own tests.
public class QuestLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static Quest Quest() {
        var quest = new Quest(TestGameSession.QuestData(0x01020304)) {
            State = QuestState.Started,
            CompletionCount = 2,
            StartTime = 0x0A,
            EndTime = 0x0B,
            Track = true,
        };
        quest.Conditions[0] = new Quest.Condition(null!) { Counter = 5 };
        quest.Conditions[1] = new Quest.Condition(null!) { Counter = 6 };
        return quest;
    }

    // Mode 2 (:1004): int id, long start, byte tracking; then CQuest::DecodeStart (questmanager_1.cpp:389): int count,
    // count ints.
    [Test]
    public void Start() {
        Assert.That(Body(QuestPacket.Start(Quest())), Is.EqualTo(new byte[] {
            0x02, 0x04, 0x03, 0x02, 0x01, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0x01,
            0x02, 0, 0, 0, 0x05, 0, 0, 0, 0x06, 0, 0, 0,
        }));
    }

    // Mode 3 (:1023): int id; then CQuest::DecodeProgress (questmanager_1.cpp:403): int count, count ints.
    [Test]
    public void Update() {
        Assert.That(Body(QuestPacket.Update(Quest())), Is.EqualTo(new byte[] {
            0x03, 0x04, 0x03, 0x02, 0x01, 0x02, 0, 0, 0, 0x05, 0, 0, 0, 0x06, 0, 0, 0,
        }));
    }

    // Mode 4 (:1034): int id, int (Set68), long complete time; CQuest::Complete reads nothing more.
    [Test]
    public void Complete() {
        Assert.That(Body(QuestPacket.Complete(Quest())), Is.EqualTo(new byte[] {
            0x04, 0x04, 0x03, 0x02, 0x01, 0x02, 0, 0, 0, 0x0B, 0, 0, 0, 0, 0, 0, 0,
        }));
    }

    // Mode 0x16 (:959): int count; per quest int id, int state, int (Set68), long start, long complete, byte tracking,
    // then int count and count ints. The client reads that last list only for a quest it knows, so the server sends
    // only those (QuestManager.Load, PlayedClientQuests).
    [Test]
    public void LoadQuestStates() {
        Assert.That(Body(QuestPacket.LoadQuestStates([Quest()])), Is.EqualTo(new byte[] {
            0x16, 0x01, 0, 0, 0,
            0x04, 0x03, 0x02, 0x01, 0x01, 0, 0, 0, 0x02, 0, 0, 0,
            0x0A, 0, 0, 0, 0, 0, 0, 0, 0x0B, 0, 0, 0, 0, 0, 0, 0, 0x01,
            0x02, 0, 0, 0, 0x05, 0, 0, 0, 0x06, 0, 0, 0,
        }));
    }

    // Mode 5 (:1050): int quest, int result. Mode 6 (:1058): int quest. Mode 7 (:1065): int count, count ints. Mode 9
    // (:1076): int quest, byte tracking.
    [Test]
    public void RemoveAndTracking() {
        Assert.Multiple(() => {
            Assert.That(Body(QuestPacket.Unknown5()), Is.EqualTo(new byte[] { 0x05, 0, 0, 0, 0, 0, 0, 0, 0 }));
            Assert.That(Body(QuestPacket.Abandon(7)), Is.EqualTo(new byte[] { 0x06, 0x07, 0, 0, 0 }));
            Assert.That(Body(QuestPacket.Expired([7, 8])), Is.EqualTo(new byte[] { 0x07, 0x02, 0, 0, 0, 0x07, 0, 0, 0, 0x08, 0, 0, 0 }));
            Assert.That(Body(QuestPacket.SetTracking(7, true)), Is.EqualTo(new byte[] { 0x09, 0x07, 0, 0, 0, 0x01 }));
        });
    }

    // Mode 0x15 (:948): int, int. Mode 0x17 (:989): int count, count ints. Mode 0x19 (:1083): long. Mode 0x1a (:1108):
    // int. Modes 0x1e (:1142) and 0x26 (:1172): nothing.
    [Test]
    public void ListsAndResets() {
        Assert.Multiple(() => {
            Assert.That(Body(QuestPacket.LoadExploration(3, 4)), Is.EqualTo(new byte[] { 0x15, 0x03, 0, 0, 0, 0x04, 0, 0, 0 }));
            Assert.That(Body(QuestPacket.LoadQuests([7])), Is.EqualTo(new byte[] { 0x17, 0x01, 0, 0, 0, 0x07, 0, 0, 0 }));
            Assert.That(Body(QuestPacket.DailyMissionReset(0x0102)), Is.EqualTo(new byte[] { 0x19, 0x02, 0x01, 0, 0, 0, 0, 0, 0 }));
            Assert.That(Body(QuestPacket.UpdateExploration(3)), Is.EqualTo(new byte[] { 0x1A, 0x03, 0, 0, 0 }));
            Assert.That(Body(QuestPacket.FieldQuestReset()), Is.EqualTo(new byte[] { 0x1E }));
            Assert.That(Body(QuestPacket.WeddingMissionReset()), Is.EqualTo(new byte[] { 0x26 }));
        });
    }
}

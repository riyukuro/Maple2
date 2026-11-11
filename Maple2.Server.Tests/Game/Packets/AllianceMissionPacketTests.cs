using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Quest 0x1F and 0x20 as CQuestManager reads them (questmanager_2.cpp:1145): the drop byte, the count, the ids.
public class AllianceMissionPacketTests {
    [TestCase(true, 31, 1)]
    [TestCase(false, 32, 0)]
    public void MissionListBytes(bool daily, int command, int drop) {
        ByteWriter packet = daily
            ? QuestPacket.DailyAllianceMissions(true, [91000101, 91000102])
            : QuestPacket.WeeklyAllianceMissions(false, [91000201, 91000202]);
        var reader = new ByteReader(packet.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Quest));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        Assert.That(reader.ReadByte(), Is.EqualTo(drop));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(daily ? 91000101 : 91000201));
        Assert.That(reader.ReadInt(), Is.EqualTo(daily ? 91000102 : 91000202));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Quest 0x22 and 0x23: the alliance as a short, the {0} of s_quest_alliance_accept_all_{0} / complete_all_{0}.
    [TestCase(true, 34)]
    [TestCase(false, 35)]
    public void AcceptAndCompleteAllBytes(bool accept, int command) {
        ByteWriter packet = accept ? QuestPacket.AllianceAccept(AllianceType.KritiasGreenHoods) : QuestPacket.AllianceComplete(AllianceType.KritiasGreenHoods);
        var reader = new ByteReader(packet.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Quest));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        Assert.That(reader.ReadShort(), Is.EqualTo(8));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // NpcTalk 8, the answer to the window's accept, as UITalkManager::OnTalkPacket mode 8 reads it: the talk type, the
    // quest id, the script state, the page, the button.
    [Test]
    public void AllianceQuestTalkBytes() {
        ByteWriter packet = NpcTalkPacket.AllianceQuestTalk(NpcTalkType.Quest, new NpcDialogue(100, 0, NpcTalkButton.QuestAccept), 91000101);
        var reader = new ByteReader(packet.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.NpcTalk));
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(91000101));
        Assert.That(reader.ReadInt(), Is.EqualTo(100));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) NpcTalkButton.QuestAccept));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

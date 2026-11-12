using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: UICutSceneCinematic 0x141909eb0 (client/ms2/uicutscenecinematic.cpp) reads byte table, int object id,
// unicode script, int duration, int delay. Table 0 resolves "$key$" in stringTrigger.xml, anything else in
// stringNpcAi.xml, so a trigger key sent with 1 shows "NO KEY". Made-up ids and keys.
public class BalloonTalkPacketTests {
    private static (byte Table, int ObjectId, string Script, int Duration, int Delay) Read(ByteWriter writer) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Cinematic));
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        return (reader.ReadByte(), reader.ReadInt(), reader.ReadUnicodeString(), reader.ReadInt(), reader.ReadInt());
    }

    [Test]
    public void TriggerTableIsZero() {
        var talk = Read(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, 9001, "$90000001_XX__MAIN__2$", 3000, 500));
        Assert.That(talk, Is.EqualTo(((byte) 0, 9001, "$90000001_XX__MAIN__2$", 3000, 500)));
    }

    [Test]
    public void NpcAiTableIsOne() {
        var talk = Read(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.NpcAi, 9002, "$AI__AI_TEST__0$", 2000, 0));
        Assert.That(talk, Is.EqualTo(((byte) 1, 9002, "$AI__AI_TEST__0$", 2000, 0)));
    }
}

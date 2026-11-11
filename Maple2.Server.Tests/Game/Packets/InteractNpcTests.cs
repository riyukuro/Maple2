using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class InteractNpcTests {
    // Client: CSceneStateGame::OnInteractNpc (client/ms2/scenestategame.cpp:3634): i32 npc object id, u8 talkable.
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void IsObjectIdThenTalkable(bool talkable, byte expected) {
        ByteWriter writer = FieldPacket.InteractNpc(900601, talkable);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.InteractNpc));
        Assert.That(reader.ReadInt(), Is.EqualTo(900601));
        Assert.That(reader.ReadByte(), Is.EqualTo(expected));
        Assert.That(writer.Length, Is.EqualTo(2 + 4 + 1));
    }
}

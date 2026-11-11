using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class PartyDungeonRoomCreateTests {
    // Client: CPartyManager::OnPartyDungeonRoomCreate (partymanager.cpp:1503), party mode 56: one int, the dungeon id.
    [Test]
    public void DungeonRoomCreateIsOneInt() {
        ByteWriter writer = PartyPacket.DungeonRoomCreate(23000001);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Party));
        Assert.That(reader.ReadByte(), Is.EqualTo(56));
        Assert.That(reader.ReadInt(), Is.EqualTo(23000001));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }
}

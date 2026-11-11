using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: CBlackMarketMan::FUN_140a21ca0 reads a byte request mode, an int code, a long listing id and two ints.
// DungeonRecordManagerCli::OnError reads an int code and an int argument; code 4 takes the dungeon room id.
public class BlackMarketAndDungeonErrorTests {
    [Test]
    public void BlackMarketErrorCarriesRequestModeAndListing() {
        ByteWriter writer = BlackMarketPacket.Error(BlackMarketError.s_err_lack_itemcount, 5, 0x1122334455L);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.BlackMarket));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.ReadByte(), Is.EqualTo(5));
            Assert.That(reader.ReadInt(), Is.EqualTo(23));
            Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455L));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4 + 8 + 4 + 4));
        });
    }

    [Test]
    public void DungeonCooldownErrorIsFourWithRoomId() {
        ByteWriter writer = DungeonRoomPacket.Error(DungeonRoomError.s_room_party_err_cooldown, 23000001);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.RoomDungeon));
            Assert.That(reader.ReadByte(), Is.EqualTo(18));
            Assert.That(reader.ReadInt(), Is.EqualTo(4));
            Assert.That(reader.ReadInt(), Is.EqualTo(23000001));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 4));
        });
    }
}

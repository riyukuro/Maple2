using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class MapleopolyPacketTests {
    // Client: UIBlueMarbleGameDialog::vfunc_79 (client/ms2/uibluemarblegamedialog.cpp:189) and the board reader
    // FUN_140f227a0 (:327): a tile is short kind, int arg, int count; an item tile is kind 10.
    [Test]
    public void MapleopolyBoardTileIsTenBytes() {
        BlueMarble.Slot[] slots = [
            new(BlueMarbleSlotType.Start, 0, new ItemComponent(0, 0, 0, ItemTag.None)),
            new(BlueMarbleSlotType.Item, 0, new ItemComponent(20000027, -1, 3, ItemTag.None)),
            new(BlueMarbleSlotType.Forward, 2, new ItemComponent(0, 0, 0, ItemTag.None)),
        ];

        ByteWriter writer = MapleopolyPacket.Load(slots, 30000100, 29, 1, 2);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mapleopoly));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(29));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(30000100));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That((reader.ReadShort(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo(((short) 6, 0, 0)));
        Assert.That((reader.ReadShort(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo(((short) 10, 20000027, 3)));
        Assert.That((reader.ReadShort(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo(((short) 3, 2, 0)));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 16 + 4 + 3 * 10));
    }

    // Kind 2: byte action, int position, 2 dice, int free dice. Kind 4: short kind, int position, int free dice,
    // int arg, int count.
    [Test]
    public void MapleopolyRollAndResult() {
        ByteWriter roll = MapleopolyPacket.Roll(31, 4, 4, 1);
        var reader = new ByteReader(roll.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mapleopoly));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(31));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(roll.Length, Is.EqualTo(2 + 1 + 1 + 16));

        var slot = new BlueMarble.Slot(BlueMarbleSlotType.Item, 0, new ItemComponent(20000027, -1, 3, ItemTag.None));
        ByteWriter result = MapleopolyPacket.Result(slot, 31, 1);
        reader = new ByteReader(result.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mapleopoly));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadShort(), Is.EqualTo(10));
        Assert.That(reader.ReadInt(), Is.EqualTo(31));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000027));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(result.Length, Is.EqualTo(2 + 1 + 2 + 16));
    }

    // A roll opens one result. A backward or forward tile keeps it open for the tile the token walks to; any
    // other tile closes it, so a repeated result request is not paid.
    [Test]
    public void MapleopolyResultIsTakenOncePerRoll() {
        var turn = new MapleopolyTurn();
        Assert.That(turn.TakeResult(BlueMarbleSlotType.Item), Is.False);

        turn.Rolled();
        Assert.That(turn.TakeResult(BlueMarbleSlotType.Item), Is.True);
        Assert.That(turn.TakeResult(BlueMarbleSlotType.Item), Is.False);

        turn.Rolled();
        Assert.That(turn.TakeResult(BlueMarbleSlotType.WorldTour), Is.True);
        Assert.That(turn.TakeResult(BlueMarbleSlotType.WorldTour), Is.False);

        turn.Rolled();
        Assert.That(turn.TakeResult(BlueMarbleSlotType.Backward), Is.True);
        Assert.That(turn.TakeResult(BlueMarbleSlotType.Forward), Is.True);
        Assert.That(turn.TakeResult(BlueMarbleSlotType.Paradise), Is.True);
        Assert.That(turn.TakeResult(BlueMarbleSlotType.Paradise), Is.False);
    }

    // A backward tile nearer to the start than its move amount, on the first lap: the position stops at 0.
    [Test]
    public void MapleopolyBackwardStopsAtStart() {
        Assert.That(MapleopolyTurn.MoveBackward(2, 3), Is.EqualTo(0));
        Assert.That(MapleopolyTurn.MoveBackward(30, 3), Is.EqualTo(27));
    }
}

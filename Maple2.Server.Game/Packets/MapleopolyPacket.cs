using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class MapleopolyPacket {
    private enum Command : byte {
        Load = 0,
        Roll = 2,
        Result = 4,
        Error = 6,
    }

    // Client: UIBlueMarbleGameDialog::vfunc_79 kind 0 reads int position, int free dice, int ticket item id,
    // int ticket count per roll, then the board: int count x (short kind, int arg, int count).
    public static ByteWriter Load(IList<BlueMarble.Slot> tiles, int ticketId, int totalTileCount, int freeRollAmount, int ticketsPerRoll) {
        var pWriter = Packet.Of(SendOp.Mapleopoly);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(totalTileCount);
        pWriter.WriteInt(freeRollAmount);
        pWriter.WriteInt(ticketId);
        pWriter.WriteInt(ticketsPerRoll);
        pWriter.WriteInt(tiles.Count);

        foreach (BlueMarble.Slot tile in tiles) {
            pWriter.WriteSlot(tile);
        }

        return pWriter;
    }

    // Client kind 2: byte action (2 = failed to leave the trap, 3 = left it), int position, 2 dice, int free dice.
    public static ByteWriter Roll(int tileLocation, int dice1, int dice2, int freeRollAmount, MapleopolyError error = MapleopolyError.ok) {
        var pWriter = Packet.Of(SendOp.Mapleopoly);
        pWriter.Write<Command>(Command.Roll);
        pWriter.WriteByte((byte) error);
        pWriter.WriteInt(tileLocation);
        pWriter.WriteInt(dice1);
        pWriter.WriteInt(dice2);
        pWriter.WriteInt(freeRollAmount);

        return pWriter;
    }

    // Client kind 4: short kind, int position, int free dice, int arg, int count (item id and count for kinds 9, 10).
    public static ByteWriter Result(BlueMarble.Slot slot, int totalTileCount, int freeRollAmount) {
        var pWriter = Packet.Of(SendOp.Mapleopoly);
        pWriter.Write<Command>(Command.Result);
        pWriter.WriteShort(ClientKind(slot.Type));
        pWriter.WriteInt(totalTileCount);
        pWriter.WriteInt(freeRollAmount);
        pWriter.WriteSlotArgs(slot);

        return pWriter;
    }

    // The client numbers the tile kinds 1-9 as BlueMarbleSlotType does, and an item tile 10; 0 is an empty tile.
    private static short ClientKind(BlueMarbleSlotType type) {
        return type == BlueMarbleSlotType.Item ? (short) 10 : (short) type;
    }

    private static void WriteSlot(this IByteWriter pWriter, BlueMarble.Slot slot) {
        pWriter.WriteShort(ClientKind(slot.Type));
        pWriter.WriteSlotArgs(slot);
    }

    private static void WriteSlotArgs(this IByteWriter pWriter, BlueMarble.Slot slot) {
        if (slot.Type is BlueMarbleSlotType.Item or BlueMarbleSlotType.Paradise) {
            pWriter.WriteInt(slot.Item.ItemId);
            pWriter.WriteInt(slot.Item.Amount);
        } else {
            pWriter.WriteInt(slot.MoveAmount);
            pWriter.WriteInt();
        }
    }

    public static ByteWriter Error(MapleopolyError error) {
        var pWriter = Packet.Of(SendOp.Mapleopoly);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<MapleopolyError>(error);

        return pWriter;
    }
}

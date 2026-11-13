using Maple2.Model.Common;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

// The parts of an item that are rolled at its creation (stats, sockets, the color and the socket-transfer flag of
// ItemDropManager.CreateItem), as bytes. A channel rolls an item that the World then creates, e.g. a guild storage
// gift, so the item comes out the way a bag item would.
public static class ItemRolls {
    public static byte[] Write(Item item) {
        var writer = new ByteWriter();
        writer.WriteBool(item.Stats != null);
        if (item.Stats != null) {
            writer.WriteClass<ItemStats>(item.Stats);
        }
        writer.WriteBool(item.Socket != null);
        if (item.Socket != null) {
            writer.WriteClass<ItemSocket>(item.Socket);
        }
        writer.WriteBool(item.Appearance != null);
        if (item.Appearance != null) {
            writer.Write<EquipColor>(item.Appearance.Color);
        }
        writer.WriteBool(item.Transfer?.SocketTransfer ?? true);
        return writer.ToArray();
    }

    // Puts the rolls on an item of the same id made bare (new Item). False, with the item unchanged, when the bytes
    // are empty or do not read.
    public static bool Apply(Item item, byte[] data) {
        if (data.Length == 0) {
            return false;
        }

        try {
            var reader = new ByteReader(data);
            ItemStats? stats = null;
            if (reader.ReadBool()) {
                stats = new ItemStats();
                stats.ReadFrom(reader);
            }
            ItemSocket? socket = null;
            if (reader.ReadBool()) {
                socket = new ItemSocket(0, 0);
                socket.ReadFrom(reader);
            }
            EquipColor? color = reader.ReadBool() ? reader.Read<EquipColor>() : null;
            bool socketTransfer = reader.ReadBool();
            if (reader.Available != 0) {
                return false;
            }

            item.Stats = stats;
            item.Socket = socket;
            if (color != null && item.Appearance != null) {
                item.Appearance.Color = color.Value;
            }
            if (item.Transfer != null) {
                item.Transfer.SocketTransfer = socketTransfer;
            }
            return true;
        } catch (Exception) {
            return false;
        }
    }
}

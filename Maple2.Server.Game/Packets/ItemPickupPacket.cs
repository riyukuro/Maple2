using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

public static class ItemPickupPacket {
    // The pickup was refused and the drop stays. Mode 1 clears the client's "pickup requested" mark on the drop
    // (CSceneStateGame::OnFieldPickupItem); with the mark left set, auto loot and pet loot skip the drop for good.
    public static ByteWriter Refuse(int objectId) {
        var pWriter = Packet.Of(SendOp.FieldPickupItem);
        pWriter.WriteBool(false);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(1);

        return pWriter;
    }

    public static ByteWriter PickupItem(int objectId, FieldPlayer player) {
        var pWriter = Packet.Of(SendOp.FieldPickupItem);
        pWriter.WriteBool(true);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(player.ObjectId);

        return pWriter;
    }

    public static ByteWriter PickupMeso(int objectId, FieldPlayer player, long amount) {
        var pWriter = Packet.Of(SendOp.FieldPickupItem);
        pWriter.WriteBool(true);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteLong(amount);

        return pWriter;
    }

    public static ByteWriter PickupStamina(int objectId, FieldPlayer player, int amount) {
        var pWriter = Packet.Of(SendOp.FieldPickupItem);
        pWriter.WriteBool(true);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteInt(amount);

        return pWriter;
    }
}

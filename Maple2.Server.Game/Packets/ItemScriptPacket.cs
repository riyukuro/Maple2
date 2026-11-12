using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class ItemScriptPacket {
    private enum Command : byte {
        LulluBox = 0,
        PetBox = 1,
        TreasureMapCost = 2,
        TreasureMapResult = 3,
        TreasureChest = 4,
        Gacha = 5,
    }

    public static ByteWriter LulluBox(ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.ItemScript);
        pWriter.Write<Command>(Command.LulluBox);
        pWriter.WriteInt(items.Count);
        foreach (Item item in items) {
            pWriter.WriteInt(item.Id);
            pWriter.WriteInt(item.Amount);
            pWriter.WriteInt(item.Rarity);
            pWriter.WriteBool(true); // Enables sparkling animation around item icon
        }

        return pWriter;
    }

    // UIItemPetBoxDialog::vfunc_79: the special flag marks the entry for the firework effect.
    public static ByteWriter PetBox(ICollection<(Item Item, bool Special)> items) {
        var pWriter = Packet.Of(SendOp.ItemScript);
        pWriter.Write<Command>(Command.PetBox);
        pWriter.WriteInt(items.Count);
        foreach ((Item item, bool special) in items) {
            pWriter.WriteInt(item.Id);
            pWriter.WriteInt(item.Amount);
            pWriter.WriteInt(item.Rarity);
            pWriter.WriteBool(special);
        }

        return pWriter;
    }

    // UIItemTreasureMapDialog: the cost of the map the player picked; a negative cost reads "cannot be appraised".
    public static ByteWriter TreasureMapCost(long itemUid, long cost) {
        var pWriter = Packet.Of(SendOp.ItemScript);
        pWriter.WriteByte((byte) Command.TreasureMapCost);
        pWriter.WriteLong(itemUid);
        pWriter.WriteLong(cost);

        return pWriter;
    }

    // The appraised item (id and rank) the dialog shows; without one the dialog goes back to its map list.
    public static ByteWriter TreasureMapResult(int itemId, int rarity) {
        var pWriter = Packet.Of(SendOp.ItemScript);
        pWriter.WriteByte((byte) Command.TreasureMapResult);
        pWriter.WriteBool(true);
        pWriter.WriteInt(itemId);
        pWriter.WriteInt(rarity);

        return pWriter;
    }

    public static ByteWriter TreasureMapFailed() {
        var pWriter = Packet.Of(SendOp.ItemScript);
        pWriter.WriteByte((byte) Command.TreasureMapResult);
        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter TreasureChest(int itemId) {
        var pWriter = Packet.Of(SendOp.ItemScript);
        pWriter.Write<Command>(Command.TreasureChest);
        pWriter.WriteInt(itemId);

        return pWriter;
    }

    public static ByteWriter Gacha(ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.ItemScript);
        pWriter.Write<Command>(Command.Gacha);
        pWriter.WriteInt(items.Count);
        foreach (Item item in items) {
            pWriter.WriteInt(item.Id);
            pWriter.WriteInt(item.Amount);
            pWriter.WriteInt(item.Rarity);
            pWriter.WriteBool(true);
        }

        return pWriter;
    }
}

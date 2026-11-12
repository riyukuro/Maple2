using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class ItemBindPacket {
    // The packet that tells the client an item it holds is now bound. A bag item gets ItemInventory mode 14, which
    // also refreshes the bag UI; the client applies mode 14 to bag items only, so any other item (equipped) gets
    // BindItem.
    public static ByteWriter For(Item item, bool inBag) {
        return inBag ? ItemInventoryPacket.UpdateItem(item) : Bind(item);
    }

    // CSceneStateGame::OnBindItem: the uid, then the item stream read over the client's item, which it looks up
    // in the bag first and among the equipped items second.
    public static ByteWriter Bind(Item item) {
        var pWriter = Packet.Of(SendOp.BindItem);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }
}

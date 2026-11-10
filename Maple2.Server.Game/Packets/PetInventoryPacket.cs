using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class PetInventoryPacket {
    private enum Command : byte {
        Add = 0,
        Remove = 1,
        Update = 2,
        Move = 3,
        Load = 4,
        Reset = 6,
    }

    // OnPetInventory mode 0 (scenestategame.cpp:2648): id, uid, slot, rarity, then the item; no string, as in Load.
    public static ByteWriter Add(Item item) {
        var pWriter = Packet.Of(SendOp.PetInventory);
        pWriter.Write<Command>(Command.Add);
        pWriter.WriteInt(item.Id);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteShort(item.Slot);
        pWriter.WriteInt(item.Rarity);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter Remove(long uid) {
        var pWriter = Packet.Of(SendOp.PetInventory);
        pWriter.Write<Command>(Command.Remove);
        pWriter.WriteLong(uid);

        return pWriter;
    }

    public static ByteWriter Update(long uid, int amount) {
        var pWriter = Packet.Of(SendOp.PetInventory);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteLong(uid);
        pWriter.WriteInt(amount);

        return pWriter;
    }

    public static ByteWriter Move(long dstUid, short srcSlot, long srcUid, short dstSlot) {
        var pWriter = Packet.Of(SendOp.PetInventory);
        pWriter.Write<Command>(Command.Move);
        pWriter.WriteLong(dstUid);
        pWriter.WriteShort(srcSlot);
        pWriter.WriteLong(srcUid);
        pWriter.WriteShort(dstSlot);

        return pWriter;
    }

    public static ByteWriter Load(ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.PetInventory);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteShort((short) items.Count);
        foreach (Item item in items) {
            pWriter.WriteInt(item.Id);
            pWriter.WriteLong(item.Uid);
            pWriter.WriteShort(item.Slot);
            pWriter.WriteInt(item.Rarity);
            pWriter.WriteClass<Item>(item);
        }

        return pWriter;
    }

    public static ByteWriter Reset() {
        var pWriter = Packet.Of(SendOp.PetInventory);
        pWriter.Write<Command>(Command.Reset);

        return pWriter;
    }
}

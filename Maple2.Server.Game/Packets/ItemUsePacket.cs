using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class ItemUsePacket {
    private enum Command : byte {
        ExpandInventory = 0,
        MaxInventory = 1,
        CharacterSlotAdded = 2,
        MaxCharacterSlots = 3,
        QuestScroll = 4,
        FashionCoupon = 5, // Codi/outfit fashion tab on player
        BeautyCoupon = 6,
    }

    // s_msg_expand_inven_complete
    // - Your inventory has been expanded.
    public static ByteWriter ExpandInventory() {
        var pWriter = Packet.Of(SendOp.ItemUse);
        pWriter.Write<Command>(Command.ExpandInventory);

        return pWriter;
    }

    // s_msg_expand_inven_already_maximum
    // - You cannot expand your inventory any further.
    public static ByteWriter MaxInventory() {
        var pWriter = Packet.Of(SendOp.ItemUse);
        pWriter.Write<Command>(Command.MaxInventory);

        return pWriter;
    }

    // s_msg_expand_character_slot_complete
    // - A character slot has been added.
    public static ByteWriter CharacterSlotAdded() {
        var pWriter = Packet.Of(SendOp.ItemUse);
        pWriter.Write<Command>(Command.CharacterSlotAdded);

        return pWriter;
    }

    // s_msg_expand_character_slot_already_maximum
    // - You have already unlocked the maximum number of character slots.
    public static ByteWriter MaxCharacterSlots() {
        var pWriter = Packet.Of(SendOp.ItemUse);
        pWriter.Write<Command>(Command.MaxCharacterSlots);

        return pWriter;
    }

    public static ByteWriter QuestScroll(int itemId) {
        var pWriter = Packet.Of(SendOp.ItemUse);
        pWriter.Write<Command>(Command.QuestScroll);
        pWriter.WriteInt(itemId);
        return pWriter;
    }

    // CSceneStateGame::OnActiveCodiTab: sets the second outfit tab's period on the actor. The actor's own client
    // also shows s_msg_item_active_codi_tab_complete_limitless (addedSeconds 0) or ..._complete_period with
    // addedSeconds in days, unless silent.
    public static ByteWriter ActiveCodiTab(int playerObjectId, long start, long end, long addedSeconds, bool silent = false) {
        var pWriter = Packet.Of(SendOp.ItemUse);
        pWriter.Write<Command>(Command.FashionCoupon);
        pWriter.WriteInt(playerObjectId);
        pWriter.WriteLong(start);
        pWriter.WriteLong(end);
        pWriter.WriteLong(addedSeconds);
        pWriter.WriteBool(silent);

        return pWriter;
    }

    public static ByteWriter BeautyCoupon(int playerObjectId, long itemUid) {
        var pWriter = Packet.Of(SendOp.ItemUse);
        pWriter.Write<Command>(Command.BeautyCoupon);
        pWriter.WriteInt(playerObjectId);
        pWriter.WriteLong(itemUid);

        return pWriter;
    }
}

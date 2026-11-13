using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class ItemMergePacket {
    private enum Command : byte {
        // Client 2525 CSceneStateGame::OnItemFusion (scenestategame.cpp:5906).
        Info = 0,
        Stage = 1,
        Select = 2,
        Choice = 4,
        Empower = 5,
        Error = 5, // Same as Empower
        Remove = 6,
    }

    // Client reader: uiitemmergeoptiondialog.cpp:799. The byte after the mode is 0 when no merge
    // result waits for the player's choice; the dialog then lets an item be picked.
    public static ByteWriter NoPendingChoice() {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Info);
        pWriter.WriteByte(0);

        return pWriter;
    }

    // Same reader: any byte but 0 and 2 is a merge result that waits for the player's choice. The dialog
    // takes the item and shows the old option against the new one.
    public static ByteWriter PendingChoice(int itemId, long itemUid, MergeRoll roll) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Info);
        pWriter.WriteByte(1);
        pWriter.WriteInt(itemId);
        pWriter.WriteLong(itemUid);
        pWriter.WriteMergeRoll(roll);

        return pWriter;
    }

    // Same reader, byte 2: the item of the waiting result is gone. The client names the item in a notice.
    public static ByteWriter PendingItemMissing(int itemId) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Info);
        pWriter.WriteByte(2);
        pWriter.WriteInt(itemId);

        return pWriter;
    }

    // Client CSceneStateGame::OnItemFusion mode 4 (scenestategame.cpp:5938): the option rolled for an item
    // that already has a merge option. The dialog shows both and asks which one to keep.
    public static ByteWriter Choice(MergeRoll roll) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Choice);
        pWriter.WriteMergeRoll(roll);

        return pWriter;
    }

    public static ByteWriter Stage(ICollection<long> crystals) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Stage);
        pWriter.WriteInt(crystals.Count);
        foreach (long uid in crystals) {
            pWriter.WriteLong(uid);
        }

        return pWriter;
    }

    public static ByteWriter Select(ItemMergeTable.Entry metadata, int materialMultiplier = 1) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Select);
        // Client 2525 CItemMergeOptionProxy::Read (itemmergeoptiontable.cpp:245).
        pWriter.WriteLong(metadata.MesoCost * materialMultiplier);
        pWriter.WriteByte((byte) metadata.Materials.Length);
        foreach (ItemComponent ingredient in metadata.Materials) {
            pWriter.WriteInt(ingredient.ItemId);
            pWriter.WriteInt(ItemTagMap.ToClient(ingredient.Tag));
            pWriter.WriteInt(ingredient.Amount * materialMultiplier);
        }

        pWriter.WriteInt(); // Unknown loop

        // Basic Attributes
        pWriter.WriteInt(metadata.BasicOptions.Count);
        foreach ((BasicAttribute attribute, ItemMergeTable.Option mergeOption) in metadata.BasicOptions) {
            pWriter.WriteShort(attribute.ClientStat());
            pWriter.WriteMergeStat(mergeOption);
        }

        // Special Attributes
        pWriter.WriteInt(metadata.SpecialOptions.Count);
        foreach ((SpecialAttribute attribute, ItemMergeTable.Option mergeOption) in metadata.SpecialOptions) {
            pWriter.WriteShort((short) attribute);
            pWriter.WriteMergeStat(mergeOption);
        }

        return pWriter;
    }

    public static ByteWriter Empower(Item item, ItemMergeError error = ItemMergeError.ok) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Empower);
        pWriter.Write<ItemMergeError>(error);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter Error(ItemMergeError error) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<ItemMergeError>(error);

        return pWriter;
    }

    public static ByteWriter Remove(Item item) {
        var pWriter = Packet.Of(SendOp.ItemMerge);
        pWriter.Write<Command>(Command.Remove);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    private static void WriteMergeStat(this IByteWriter pWriter, ItemMergeTable.Option option) {
        ItemMergeTable.Range<int> bounds = option.Bounds();
        pWriter.WriteInt(bounds.Min);
        pWriter.WriteInt(bounds.Max);
    }

    // UIItemMergeOptionDialog::FUN_1419d1490 (uiitemmergeoptiondialog.cpp:864): basic attribute or -1,
    // special attribute (read when the first is -1), value.
    private static void WriteMergeRoll(this IByteWriter pWriter, MergeRoll roll) {
        pWriter.WriteInt(roll.Basic != null ? (int) roll.Basic.Value : -1);
        pWriter.WriteInt(roll.Basic != null ? 0 : (int) roll.Special);
        pWriter.WriteInt(roll.Value);
    }
}

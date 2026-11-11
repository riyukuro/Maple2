using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class ChangeAttributesPacket {
    private enum Command : byte {
        Preview = 1,
        Select = 2,
        StageItem = 4,
        // Client: UIItemRemakeOptionDialog::vfunc_79 reads the error as kind 5.
        Error = 5,
        Pending = 6,
    }

    // Client: UIItemRemakeOptionDialog::FUN_14129f940 (unnamed/unit_14129cfa0_itemremakeoptiondialog.cpp:626):
    // long item uid, then CIngredientContainer (commonfiles/commonstruct_wa.cpp:255): byte count, count x
    // (int item id, int tag, int count). The dialog shows the list as the cost of the next change.
    public static ByteWriter StageItem(long itemUid, ICollection<IngredientInfo> ingredients) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.StageItem);
        pWriter.WriteLong(itemUid);
        pWriter.WriteByte((byte) ingredients.Count);
        foreach (IngredientInfo ingredient in ingredients) {
            pWriter.WriteInt(ingredient.Unknown); // item id; 0 when the ingredient is named by its tag
            pWriter.WriteInt((int) ingredient.Tag);
            pWriter.WriteInt(ingredient.Amount);
        }

        return pWriter;
    }

    // Client: UIItemRemakeOptionDialog::FUN_1412a2c50 (:1069) and the pet tab
    // (TabContentsPetRemakeOption::OnRemakeResult, tabcontentspetremakeoption.cpp:1450): byte result. 0: no
    // change is pending, nothing follows.
    public static ByteWriter NoPending() {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Pending);
        pWriter.WriteByte(0);

        return pWriter;
    }

    // Result 2: int item id of the pending item, which the player no longer has.
    public static ByteWriter PendingItemGone(int itemId) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Pending);
        pWriter.WriteByte(2);
        pWriter.WriteInt(itemId);

        return pWriter;
    }

    // Any other result: int item id, long item uid, then the same block as the preview (long item uid and the
    // item with its new options). The dialog shows the pending choice again.
    public static ByteWriter PendingItem(Item item) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Pending);
        pWriter.WriteByte(1);
        pWriter.WriteInt(item.Id);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter PreviewItem(Item item) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Preview);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter SelectItem(Item item) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Select);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter Error(ChangeAttributesError error, bool flag = false) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteBool(flag); // false: the dialog leaves its waiting state
        pWriter.WriteInt((int) error);

        return pWriter;
    }
}

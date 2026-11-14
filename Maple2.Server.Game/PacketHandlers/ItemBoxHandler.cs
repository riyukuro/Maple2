using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemBoxHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemBox;

    public override void Handle(GameSession session, IByteReader packet) {
        (int itemId, short rarity, int count, string selection) = ReadRequest(packet);
        if (!ItemBoxRules.IsOpenCount(count, session.ServerTableMetadata.ConstantsTable.ItemBoxMultiOpenMaxCount)) {
            return;
        }

        // The client names the box by id and rarity, the two it keeps for the dialog (UIItemMultiOpenBoxDialog
        // FUN_140c3f9d0 is given the item's id and GetRarity()), so the stack is picked by both. A rarity below 0,
        // which Find takes as any rarity, is never sent by the client and is dropped.
        if (rarity < 0) {
            return;
        }
        // The stack must pass the checks of an item use (ItemUseHandler.UseAllowed): the dialog sends again after each
        // answer without asking CanUseItem, so a box that expired or a premium club that ended while it ran is still
        // sent. Chosen here: a box held but not usable is answered "Unable to open boxes" (3), which closes the dialog,
        // as the client sends nothing for it from the bag.
        // Filter, as Find skips an expired stack.
        IList<Item> held = session.Item.Inventory.Filter(stack => stack.Id == itemId && stack.Rarity == rarity);
        if (held.Count == 0) {
            return;
        }
        // Only a box the dialog opens: the client reads the answer only for OpenItemBox and SelectItemBox
        // (CSceneStateGame::OnItemBox), and the key box goes the same way. Any other item is dropped. Its function
        // parameters are not a box's (int.Parse threw on them and ended the session), and a gacha or timed box
        // would skip its own dialog and cooldown.
        if (held[0].Metadata.Function?.Type is not (ItemFunction.OpenItemBox or ItemFunction.SelectItemBox or ItemFunction.OpenItemBoxWithKey)) {
            return;
        }
        Item? item = held.FirstOrDefault(stack => ItemUseHandler.UseAllowed(session, stack));
        if (item == null) {
            session.Send(ItemBoxPacket.Open(itemId, 0, ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail));
            return;
        }

        // A select box's picked rows; ItemBoxManager checks them.

        ItemBoxError error = session.ItemBox.Open(item, count, selection);
        session.Send(ItemBoxPacket.Open(itemId, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    // UIItemMultiOpenBoxDialog::vfunc_15 (uiitemmultiopenboxdialog.cpp:137-141): int box id, short rarity, int count,
    // then the select box's rows as a unicode string.
    public static (int ItemId, short Rarity, int Count, string Selection) ReadRequest(IByteReader packet) {
        int itemId = packet.ReadInt();
        short rarity = packet.ReadShort();
        int count = packet.ReadInt();
        string selection = packet.ReadUnicodeString();
        return (itemId, rarity, count, selection);
    }
}

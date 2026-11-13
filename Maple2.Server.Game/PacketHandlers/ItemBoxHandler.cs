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
        Item? item = session.Item.Inventory.Find(itemId, rarity).FirstOrDefault();
        if (item == null) {
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

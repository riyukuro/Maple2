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
        int itemId = packet.ReadInt();
        short unk = packet.ReadShort();
        int count = packet.ReadInt();
        if (!ItemBoxRules.IsOpenCount(count, session.ServerTableMetadata.ConstantsTable.ItemBoxMultiOpenMaxCount)) {
            return;
        }

        Item? item = session.Item.Inventory.Find(itemId).FirstOrDefault();
        if (item == null) {
            return;
        }

        // A select box's picked rows; ItemBoxManager checks them.
        string selection = packet.ReadUnicodeString();

        ItemBoxError error = session.ItemBox.Open(item, count, selection);
        session.Send(ItemBoxPacket.Open(itemId, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class SuperChatHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SuperWorldChat;

    private enum Command : byte {
        Select = 0,
        Deselect = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Select:
                HandleSelect(session, packet);
                return;
            case Command.Deselect:
                HandleDeselect(session);
                return;
        }
    }

    private static void HandleSelect(GameSession session, IByteReader packet) {
        int itemId = packet.ReadInt();

        // The selection dialog lists the super chat and the wedding megaphone items (UISelectSuperWorldChatDialog,
        // GetWeddingChatItems uisidepopuptalk_2.cpp:211) and selects either with this packet; with a wedding item
        // selected the client sends its line as chat kind 25.
        Item? superChatItem = session.Item.Inventory.Find(itemId).FirstOrDefault();
        if (superChatItem?.Metadata.Function?.Type is not (ItemFunction.SuperWorldChat or ItemFunction.WeddingChat)) {
            return;
        }

        // The item id, not its first function parameter (the icon name): the client looks the line's int up as item
        // data (texttohtml FUN_14183eba0), and KMS2 lines carry the item id (20801012, whose icon parameter is 20800026).
        session.SuperChatId = itemId;
        session.SuperChatItemId = itemId;
        session.Send(SuperChatPacket.Select(session.Player.ObjectId, itemId));
    }

    private static void HandleDeselect(GameSession session) {
        session.SuperChatId = 0;
        session.SuperChatItemId = 0;
        session.Send(SuperChatPacket.Deselect(session.Player.ObjectId));
    }
}

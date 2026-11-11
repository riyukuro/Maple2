using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class FurnishingStorageHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.FurnishingStorage;

    private enum Command : byte {
        Retrieve = 4,
        Delete = 6,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Delete:
                HandleDelete(session, packet);
                return;
            case Command.Retrieve:
                // Not handled: CNetworkService::SendRetrieveFurnishingItem (networkservice.cpp:497) moves a stored furnishing item, whole
                // count, to a bag slot the client picked. This server keeps every furnishing item in the
                // furnishing storage (InventoryManager.Add sends one straight back), so there is no bag to move
                // it to; the request is dropped and the item stays where it is.
                Logger.Debug("FurnishingStorage {Command} from {CharacterId}: item {ItemUid}, slot {Slot}, amount {Amount}", command, session.CharacterId, packet.ReadLong(), packet.ReadShort(), packet.ReadInt());
                return;
        }
    }

    private static void HandleDelete(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        session.Item.Furnishing.RemoveItem(itemUid);
    }
}

using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemEquipHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemEquip;

    private enum Command : byte {
        Equip = 0,
        Unequip = 1,
        Equip2 = 2,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Equip:
                HandleEquip(session, packet);
                return;
            case Command.Unequip:
                HandleUnequip(session, packet);
                return;
            case Command.Equip2:
                return;
        }
    }

    private void HandleEquip(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        var equipSlot = packet.Read<EquipSlot>();
        // The tab the item goes to: 0 gear, 1 outfit, 2 the second outfit tab (Unk_Inventory::RequestEquipItem).
        byte kind = packet.ReadByte();

        // Disconnect if this fails to avoid bad state.
        if (session.Item.Equips.Equip(itemUid, equipSlot, kind)) {
            session.Stats.Refresh();
        }
    }

    private void HandleUnequip(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        // Disconnect if this fails to avoid bad state.
        if (!session.Item.Equips.Unequip(itemUid)) {
            Logger.Fatal("Failed to unequip item: {Uid}", itemUid);
            session.Disconnect();
        }
    }

    // The client sends this when a worn item is dragged onto another equip slot (EquipSlot::vfunc_18).
    private void HandleEquip2(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        var equipSlot = packet.Read<EquipSlot>();
    }
}

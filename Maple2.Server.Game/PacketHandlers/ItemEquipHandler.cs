using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
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
                HandleEquip2(session, packet);
                return;
        }
    }

    private void HandleEquip(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        var equipSlot = packet.Read<EquipSlot>();
        // The tab the client asks for: 0 gear, 1 outfit, 2 the second outfit tab (Unk_Inventory::RequestEquipItem);
        // a right click on an outfit item asks for 2. EquipManager picks the tab the item can go to.
        byte kind = packet.ReadByte();

        // Disconnect if this fails to avoid bad state.
        if (session.Item.Equips.Equip(itemUid, equipSlot, kind)) {
            session.Stats.Refresh();
        }
    }

    private void HandleUnequip(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        // The client sends only a worn item's uid (Unk_Inventory::RequestUnequipItem finds it on the player first),
        // so any other is dropped (a badge comes off by its own packet). It checks the bag first (FUN_140a655b0,
        // notice 67): a full bag gets the bag's own notice, as an equip that needs room, and the item stays worn.
        // Both used to cut the session.
        Item? worn = session.Item.Equips.Get(itemUid);
        if (worn == null || session.Item.Equips.Badge.Values.Contains(worn)) {
            return;
        }
        if (!session.Item.Equips.Unequip(itemUid)) {
            session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
        }
    }

    // The client sends this when a worn item is dragged onto another equip slot (EquipSlot::vfunc_18):
    // the two hand items change places. A request the client's own test would refuse is ignored.
    private void HandleEquip2(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        var equipSlot = packet.Read<EquipSlot>();

        session.Item.Equips.SwapHands(itemUid, equipSlot);
    }
}

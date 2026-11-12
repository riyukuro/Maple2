using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Packets;
using Maple2.Model.Game;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.PacketHandlers;

public class BadgeEquipHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.BadgeEquip;

    private enum Command : byte {
        Equip = 0,
        Unequip = 1,
        Transparency = 3,
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
            case Command.Transparency:
                HandleTransparency(session, packet);
                return;
        }
    }

    private void HandleEquip(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        packet.Read<BadgeType>(); // We are using static slots based on badge type

        // The client has its own error lines for a refused badge (mode 4).
        Item? badge = session.Item.Inventory.Get(itemUid);
        BadgeEquipError? error = BadgeRules.EquipError(badge != null, badge?.Badge != null, badge?.IsExpired() ?? false);
        if (error != null) {
            session.Send(EquipPacket.BadgeError(error.Value));
            return;
        }

        // A false here has already sent its own message box (level, job, binding).
        session.Item.Equips.EquipBadge(itemUid);
    }

    private void HandleUnequip(GameSession session, IByteReader packet) {
        var slot = packet.Read<BadgeType>();

        bool knownSlot = Enum.IsDefined<BadgeType>(slot);
        BadgeEquipError? error = BadgeRules.UnequipError(knownSlot, knownSlot && session.Item.Equips.Get(slot) != null,
            session.Item.Inventory.FreeSlots(InventoryType.Badge));
        if (error != null) {
            session.Send(EquipPacket.BadgeError(error.Value));
            return;
        }

        if (!session.Item.Equips.UnequipBadge(slot)) {
            session.Send(EquipPacket.BadgeError(BadgeEquipError.Unknown));
        }
    }

    private void HandleTransparency(GameSession session, IByteReader packet) {
        var badgeType = packet.Read<BadgeType>();

        Item? badgeItem = session.Item.Equips.Get(badgeType);

        if (badgeItem?.Badge == null || badgeItem.Badge.Type != BadgeType.Transparency) {
            session.Cut();
            return;
        }

        for (int i = 0; i < badgeItem.Badge.Transparency.Length; i++) {
            badgeItem.Badge.Transparency[i] = packet.ReadBool();
        }

        session.Field?.Broadcast(EquipPacket.EquipBadge(session.Player, badgeItem));
    }
}

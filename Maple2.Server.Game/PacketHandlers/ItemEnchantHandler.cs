using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemEnchantHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemEnchant;

    private enum Command : byte {
        OpenDialog = 0,
        StageItem = 1,
        UpdateFodder = 2,
        UpdateCharges = 3,
        Ophelia = 4,
        Peachy = 6,
        ConfirmRefund = 9,
        Unknown13 = 13,
        SimulateEnchant = 14,
        Unknown15 = 15,
        DebugEnchant16 = 16,
        DebugEnchant17 = 17,
        DebugEnchant18 = 18,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.OpenDialog:
                HandleOpenDialog(session);
                return;
            case Command.StageItem:
                HandleStageItem(session, packet);
                return;
            case Command.UpdateFodder:
                HandleUpdateFodder(session, packet);
                return;
            case Command.UpdateCharges:
                HandleUpdateCharges(session, packet);
                return;
            case Command.Ophelia:
                HandleOphelia(session, packet);
                return;
            case Command.Peachy:
                HandlePeachy(session, packet);
                return;
            case Command.Unknown13:
                HandleUnknown13(session, packet);
                return;
            case Command.ConfirmRefund:
            case Command.DebugEnchant17:
            case Command.DebugEnchant18:
                // Not handled: one item uid.
                Logger.Debug("ItemEnchant {Command} from {CharacterId}: item {ItemUid}", command, session.CharacterId, packet.ReadLong());
                return;
            case Command.SimulateEnchant:
                // Not handled: the simulate dialog's three values (int, int, short) and one more int.
                Logger.Debug("ItemEnchant {Command} from {CharacterId}: {A} {B} {C} {D}", command, session.CharacterId, packet.ReadInt(), packet.ReadInt(), packet.ReadShort(), packet.ReadInt());
                return;
            case Command.DebugEnchant16:
                // Not handled: item uid and an int (0 or 10).
                Logger.Debug("ItemEnchant {Command} from {CharacterId}: item {ItemUid}, {Value}", command, session.CharacterId, packet.ReadLong(), packet.ReadInt());
                return;
            case Command.Unknown15:
                HandleUnknown15(session);
                return;
        }
    }

    private static void HandleOpenDialog(GameSession session) {
        session.ItemEnchant.Reset();
    }

    private static void HandleStageItem(GameSession session, IByteReader packet) {
        var type = packet.Read<EnchantType>();
        long itemUid = packet.ReadLong();

        session.ItemEnchant.StageItem(type, itemUid);
    }

    private static void HandleUpdateFodder(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        bool add = packet.ReadBool();

        session.ItemEnchant.UpdateFodder(itemUid, add);
    }

    private static void HandleUpdateCharges(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();

        session.ItemEnchant.UpdateCharges(count);
    }

    private static void HandleOphelia(GameSession session, IByteReader packet) {
        if (session.ItemEnchant.Type != EnchantType.Ophelia) {
            return;
        }

        long itemUid = packet.ReadLong();
        packet.ReadLong(); // 0
        session.ItemEnchant.Enchant(itemUid);
    }

    private static void HandlePeachy(GameSession session, IByteReader packet) {
        if (session.ItemEnchant.Type != EnchantType.Peachy) {
            return;
        }

        long itemUid = packet.ReadLong();
        session.ItemEnchant.Enchant(itemUid);
    }

    private static void HandleUnknown13(GameSession session, IByteReader packet) {
        // The simulate dialog's selection: int, int, short.
        packet.ReadInt();
        packet.ReadInt();
        packet.ReadShort();
    }

    private static void HandleUnknown15(GameSession session) {

    }
}

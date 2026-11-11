using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

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
        Refund = 9,
        SimulateSelect = 13,
        SimulateEnchant = 14,
        // 15 to 19 come only from GM debug buttons of the enchant dialog (GM level 50 or more).
        DebugForceEnchant = 15,
        DebugFillIngredient = 16,
        DebugFillFailStack = 17,
        DebugUseAllFailStack = 18,
        Debug19 = 19,
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
            case Command.Refund:
                HandleRefund(session, packet);
                return;
            case Command.SimulateSelect:
                HandleSimulateSelect(session, packet);
                return;
            case Command.SimulateEnchant:
                HandleSimulateEnchant(session, packet);
                return;
            case Command.DebugForceEnchant:
                // UIItemEnchantDialog "onForceEnchant": no body.
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                session.ItemEnchant.DebugForceEnchant();
                return;
            case Command.Debug19:
                // The client has a sender for 19 (no body) that nothing calls.
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                Logger.Debug("Enchant debug request {Command} from {CharacterId} not implemented", command, session.CharacterId);
                return;
            case Command.DebugFillIngredient:
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                HandleDebugFillIngredient(session, packet);
                return;
            case Command.DebugFillFailStack:
            case Command.DebugUseAllFailStack:
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                HandleDebugFailStack(session, packet, command);
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

    // Client SendRequestItemEnchant (0x1419028d0): button 0 of the refund box, with the item from the box.
    // The answer is ItemEnchantPacket.Refund (enchant exp reset and materials refunded); which materials
    // come back is not known, so nothing is refunded yet.
    private void HandleRefund(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        Logger.Debug("Enchant refund of item {ItemUid} from {CharacterId} not implemented", itemUid, session.CharacterId);
    }

    // Client UIItemEnchantSimulateDialog OnSelect: the three values of the selected simulation entry.
    private void HandleSimulateSelect(GameSession session, IByteReader packet) {
        int value1 = packet.ReadInt();
        int value2 = packet.ReadInt();
        short value3 = packet.ReadShort();
        Logger.Debug("Enchant simulation select {Value1} {Value2} {Value3} from {CharacterId} not implemented",
            value1, value2, value3, session.CharacterId);
    }

    // Client UIItemEnchantSimulateDialog OnEnchant: sent after an OpenDialog (0) with the same three values
    // as SimulateSelect and a sixth dialog value.
    private void HandleSimulateEnchant(GameSession session, IByteReader packet) {
        int value1 = packet.ReadInt();
        int value2 = packet.ReadInt();
        short value3 = packet.ReadShort();
        int value6 = packet.ReadInt();
        Logger.Debug("Enchant simulation {Value1} {Value2} {Value3} {Value6} from {CharacterId} not implemented",
            value1, value2, value3, value6, session.CharacterId);
    }

    // GM buttons m_debugBtnFillIngredient (amount 0) and m_debugBtnFillIngredientUnit (amount 10).
    private void HandleDebugFillIngredient(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        int amount = packet.ReadInt();
        session.ItemEnchant.DebugFillIngredients(itemUid, amount);
    }

    // GM buttons m_debugBtnFillFailStack (17) and m_debugBtnUseAllFailStack (18).
    private void HandleDebugFailStack(GameSession session, IByteReader packet, Command command) {
        long itemUid = packet.ReadLong();
        session.ItemEnchant.DebugFailCharges(itemUid, command == Command.DebugFillFailStack);
    }
}

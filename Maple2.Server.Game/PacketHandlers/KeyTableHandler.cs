using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class KeyTableHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.KeyTable;

    // Client writers: CKeyTable::SendKeyTableMode1 (keytable.cpp:385) and SendKeysToServer (keytable.cpp:407) for 1
    // and 2, SendKeyTableMode7 (keytable.cpp:2031) for 7; the quick slot modes are in
    // unit_14092f420_accountsessionstate.cpp: 3 at :405, 4 at :468, 5 at :493 (SendKeyTableMode5), 6 at :507
    // (SendKeyTableMode6), 8 at :516 (SendKeyTableMode8).
    private enum Command : byte {
        SetGameKeyBind = 1,
        SetKeyBind = 2,
        MoveQuickSlot = 3,
        AddQuickSlot = 4,
        RemoveQuickSlot = 5,
        Unknown6 = 6,
        LeaveSkillSet = 7,
        SetActiveHotBar = 8,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        switch (command) {
            case Command.SetGameKeyBind:
            case Command.SetKeyBind:
                SetKeyBinds(session, packet);
                return;
            case Command.MoveQuickSlot:
            case Command.AddQuickSlot:
                SetQuickSlot(session, packet);
                return;
            case Command.RemoveQuickSlot:
                RemoveQuickSlot(session, packet);
                return;
            case Command.Unknown6:
                // SendKeyTableMode6 (0x140930040) writes the mode alone, and nothing in the client calls it.
                break;
            case Command.LeaveSkillSet:
                // CKeyTable::SendKeyTableMode7, on leaving the skill set mode. The client sends its keys with
                // SetKeyBind (2) just before, which is stored there, so nothing is left to do.
                break;
            case Command.SetActiveHotBar:
                SetActiveHotBar(session, packet);
                return;
        }
    }

    private void SetKeyBinds(GameSession session, IByteReader packet) {
        foreach (KeyBind keyBind in ReadKeyBinds(packet)) {
            if (!KeyBindRules.CanStore(keyBind.KeyCode, Target.LOCALE, session.Config.KeyBindCount, session.Config.HasKeyBind(keyBind.KeyCode))) {
                continue;
            }
            session.Config.SetKeyBind(keyBind);
        }
    }

    private void SetQuickSlot(GameSession session, IByteReader packet) {
        (short index, QuickSlot quickSlot, int targetSlot) = ReadQuickSlotMove(packet);
        if (!session.Config.TryGetHotBar(index, out HotBar? targetHotBar)) {
            Logger.Warning("Invalid HotBar: {Index}", index);
            return;
        }

        if (quickSlot.ItemUid != 0) {
            Item? item = session.Item.Inventory.Get(quickSlot.ItemUid);
            if (item == null || item.Id != quickSlot.ItemId) {
                Logger.Error("Assigning invalid item to QuickSlot");
                return;
            }
        }

        targetHotBar.MoveQuickSlot(targetSlot, quickSlot);
        session.Config.LoadHotBars();
    }

    private void RemoveQuickSlot(GameSession session, IByteReader packet) {
        short index = packet.ReadShort();
        if (!session.Config.TryGetHotBar(index, out HotBar? targetHotBar)) {
            Logger.Warning("Invalid HotBar: {Index}", index);
            return;
        }

        int skillId = packet.ReadInt();
        long itemUid = packet.ReadLong();
        if (targetHotBar.RemoveQuickSlot(skillId, itemUid)) {
            session.Config.LoadHotBars();
        }
    }

    private void SetActiveHotBar(GameSession session, IByteReader packet) {
        short hotBarIndex = packet.ReadShort();

        session.Config.SetActiveHotBar(hotBarIndex);
    }

    // Modes 1 and 2: int count, count x (int key, int type, long code, int slot, byte priority).
    public static List<KeyBind> ReadKeyBinds(IByteReader packet) {
        int count = packet.ReadInt();
        var keyBinds = new List<KeyBind>();
        for (int i = 0; i < count; i++) {
            int keyCode = packet.ReadInt();
            int optionType = packet.ReadInt();
            long optionGuid = packet.ReadLong();
            int slot = packet.ReadInt();
            byte priority = packet.ReadByte();
            keyBinds.Add(new KeyBind(keyCode, optionType, optionGuid, slot, priority));
        }
        return keyBinds;
    }

    // Modes 3 and 4: short hot bar, int skill, int item, long item uid, int target slot (-1 from mode 4).
    public static (short HotBar, QuickSlot Slot, int TargetSlot) ReadQuickSlotMove(IByteReader packet) {
        short hotBar = packet.ReadShort();
        int skillId = packet.ReadInt();
        int itemId = packet.ReadInt();
        long itemUid = packet.ReadLong();
        int targetSlot = packet.ReadInt();
        return (hotBar, new QuickSlot(skillId, itemId, itemUid), targetSlot);
    }
}

using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Config;

namespace Maple2.Server.Game.Packets;

// Client: CKeyTable::OnKeyTable (keytable.cpp:285) reads byte mode; 0: bool default, and when not default int count x
// (int key, int type, long code, int slot, byte priority) (:313-318), then the hot bars; 7: the hot bars alone
// (:340). Hot bars: short active page, short page count, page count x (int count, count x (int slot, int skill,
// int item, long item uid)) (:326-336). No other mode is read.
public static class KeyTablePacket {
    private enum Command : byte {
        Load = 0,
        LoadHotBar = 7,
    }

    public static ByteWriter LoadDefault() {
        var pWriter = Packet.Of(SendOp.KeyTable);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteBool(true);

        return pWriter;
    }

    public static ByteWriter Load(ICollection<KeyBind> keyBinds, short activeHotBar, IReadOnlyList<HotBar> hotBars) {
        var pWriter = Packet.Of(SendOp.KeyTable);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteBool(false);
        // KeyBind
        pWriter.WriteInt(keyBinds.Count);
        foreach (KeyBind keyBind in keyBinds) {
            pWriter.WriteInt(keyBind.KeyCode);
            pWriter.WriteInt(keyBind.OptionType);
            pWriter.WriteLong(keyBind.OptionGuid);
            pWriter.WriteInt(keyBind.Unknown1);
            pWriter.WriteByte(keyBind.Priority);
        }
        // HotBar
        pWriter.WriteHotBars(activeHotBar, hotBars);

        return pWriter;
    }

    public static ByteWriter LoadHotBar(short activeHotBar, IReadOnlyList<HotBar> hotBars) {
        var pWriter = Packet.Of(SendOp.KeyTable);
        pWriter.WriteByte((byte) Command.LoadHotBar);
        pWriter.WriteHotBars(activeHotBar, hotBars);

        return pWriter;
    }

    private static void WriteHotBars(this IByteWriter pWriter, short activeHotBar, IReadOnlyList<HotBar> hotBars) {
        pWriter.WriteShort(activeHotBar);
        pWriter.WriteShort((short) hotBars.Count);
        foreach (HotBar hotBar in hotBars) {
            pWriter.WriteInt(hotBar.Slots.Length);
            for (int i = 0; i < hotBar.Slots.Length; i++) {
                QuickSlot slot = hotBar.Slots[i];
                pWriter.WriteInt(i);
                pWriter.WriteInt(slot.SkillId);
                pWriter.WriteInt(slot.ItemId);
                pWriter.WriteLong(slot.ItemUid);
            }
        }
    }
}

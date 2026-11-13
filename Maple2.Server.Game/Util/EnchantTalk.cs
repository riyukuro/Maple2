using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The enchant dialog's talk events (UIItemModifyDialogBase::vfunc_85 and UIItemEnchantDialog::vfunc_85,
/// uiitemenchantdialog.cpp:407, 564). NpcTalk 4 is sent while the player talks to a field npc (the enchant npc's
/// function, e.g. script 30 of 11000508); NpcTalk 6, with the npc id and the enchant master script id first, only for
/// a talk without a field npc (UITalkManager::FUN_141b213c0: a special npc or talk flag 0x10). Both then carry the
/// event as a short; the item choice (event 1, UIItemEnchantDialog::OnSelectItem) adds the item uid.
/// </summary>
public static class EnchantTalk {
    public readonly record struct Event(ScriptEventType Type, long ItemUid);

    // The body of NpcTalk 4 (after the mode byte).
    public static Event Read(IByteReader packet) {
        var type = (ScriptEventType) packet.ReadShort();
        long itemUid = type == ScriptEventType.EnchantSelect ? packet.ReadLong() : 0;
        return new Event(type, itemUid);
    }
}

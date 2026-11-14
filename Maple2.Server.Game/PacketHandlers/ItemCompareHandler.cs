using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The GM item window's compare button (UIItemCreateDialog OnCompareItem, SendRequestItemCompare): the same
// CCCompareItemPacket as the repeat tool, its count left at the packet's default of 100. The client has no answer of
// its own for it; the server rolls the options as for ItemRepeat and sends them through 0x101 ItemOptionSimulator.
// GM only. No item is given.
public class ItemCompareHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemCompare;

    public override void Handle(GameSession session, IByteReader packet) {
        if (!GmCheck.Allow(session, OpCode, GmCreateRules.ItemPermission)) {
            return;
        }

        GmItemCreateRequest request = GmItemCreateRequest.Read(packet);
        int count = packet.ReadInt();
        ItemRepeatHandler.Simulate(session, request, count, Logger, "ItemCompare");
    }
}

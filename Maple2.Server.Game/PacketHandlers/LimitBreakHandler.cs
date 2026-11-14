using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// On the field thread: the limit break takes items and mesos and changes the item, which the field thread's handlers
// change too.
public class LimitBreakHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.LimitBreak;

    private enum Command : byte {
        StageItem = 0,
        LimitBreak = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.StageItem:
                HandleStageItem(session, packet);
                return;
            case Command.LimitBreak:
                HandleLimitBreak(session, packet);
                return;
        }
    }

    private static void HandleStageItem(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        session.ItemEnchant.StageLimitBreakItem(itemUid);
    }

    private static void HandleLimitBreak(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        session.ItemEnchant.LimitBreakEnchant(itemUid);
    }
}

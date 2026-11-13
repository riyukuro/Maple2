using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// SpectateMushtopia (0xAB) from the restored client's survival windows: u8 0 + the character to watch
// (the result window's watch, the killer the kill log kept), u8 3 when the observer window closes (the client then
// leaves the field itself). A fallen participant of a running game watches a living one: SpectateMushtopia 0x11A mode
// 1 puts its PC in the observer state.
public class SpectateMushtopiaHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SpectateMushtopia;

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        if (mode != 0) {
            return;
        }
        long wanted = packet.ReadLong();
        long target = session.Field?.Survival?.WatchTarget(session.CharacterId, wanted) ?? 0;
        if (target != 0) {
            session.Send(MapleSurvivalPacket.Spectate(target));
        }
    }
}

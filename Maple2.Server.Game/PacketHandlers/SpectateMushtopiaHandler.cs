using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// SpectateMushtopia (0xAB) from the restored client's survival windows: u8 0 + the character to watch
// (the result window's watch, the killer the kill log kept), u8 2 + the character when the observer switches whom it
// watches (StateObserver::FUN_140ac4c90, stateobserver.cpp:513: the client moves its camera first and keeps the new
// target), u8 3 when the observer window closes (the client then leaves the field itself). A fallen participant of a
// running game watches a living one: SpectateMushtopia 0x11A mode 1 puts its PC in the observer state.
public class SpectateMushtopiaHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SpectateMushtopia;

    private const byte Watch = 0;
    private const byte Switch = 2;

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        if (mode is not (Watch or Switch)) {
            return;
        }
        long wanted = packet.ReadLong();
        long pick = session.Field?.Survival?.WatchTarget(session.CharacterId, wanted) ?? 0;
        long answer = Answer(mode == Switch, wanted, pick);
        if (answer != 0) {
            session.Send(MapleSurvivalPacket.Spectate(answer));
        }
    }

    // The target to send back (0: none). Watch answers with the server's pick. Chosen here: a switch is answered only
    // when the pick differs from the character the client already turned to (it fell or left, or is no participant),
    // so the observer is moved onto one still playing; no client code says the server answers a switch.
    public static long Answer(bool isSwitch, long wanted, long pick) {
        if (pick == 0 || (isSwitch && pick == wanted)) {
            return 0;
        }
        return pick;
    }
}

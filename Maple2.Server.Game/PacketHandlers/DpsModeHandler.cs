using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The DPS meter window sends 1 when it opens and 0 when it closes (UIDPSDialog, uidpsdialog.cpp:86 and :98). The
// server keeps the flag so that only an open meter gets DpsStat.
public class DpsModeHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.DpsMode;

    public override void Handle(GameSession session, IByteReader packet) {
        session.DpsMeterOpen = packet.ReadBool();
    }
}

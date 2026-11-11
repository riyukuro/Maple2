using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class GlobalFactorHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.GlobalFactor;

    public override void Handle(GameSession session, IByteReader packet) {
        // CSceneStateGame sends this with no fields on every server entry until it has read the rates
        // once; the reply sets them and stops the requests.
        session.Send(GlobalFactorPacket.Rates());
    }
}

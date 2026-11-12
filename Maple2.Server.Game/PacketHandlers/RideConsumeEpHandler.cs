using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The rider dashes on its mount (empty body, as in KMS2); the ride's dash stamina is taken.
public class RideConsumeEpHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RideConsumeEp;

    public override void Handle(GameSession session, IByteReader packet) {
        session.Ride.ConsumeDashStamina();
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class UserSyncHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.UserSync;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.State != SessionState.Connected || session.Field == null) {
            return;
        }

        packet.ReadByte(); // Unknown
        packet.ReadInt(); // ServerTicks
        packet.ReadInt(); // ClientTicks

        byte segments = packet.ReadByte();
        if (segments == 0) {
            return;
        }

        var stateSyncs = new StateSync[segments];
        for (int i = 0; i < segments; i++) {
            var playerState = packet.Peek<ActorState>();
            StateSync stateSync = playerState switch {
                ActorState.CoupleEmotion => new StateSyncCoupleEmotion(),
                ActorState.MicroGameRps => new StateSyncRps(),
                ActorState.MicroGameCoupleDance => new StateSyncCoupleDance(),
                ActorState.WeddingEmotion => new StateSyncWeddingEmotion(),
                _ => new StateSync()
            };

            stateSync.ReadFrom(packet);
            // The other clients' reader fails the whole packet on a sub state of 179 or more, or on the byte
            // after the speed outside 0..0x13: such a record is not sent on.
            if (!StateSyncRules.IsRelayable((byte) stateSync.SubState, stateSync.Unknown1)) {
                return;
            }
            stateSyncs[i] = stateSync;

            packet.ReadInt(); // ClientTicks
        }

        using (var buffer = new PoolByteWriter()) {
            buffer.Player(session.Player.ObjectId, stateSyncs);
            session.Field?.Broadcast(buffer, sender: session);
        }

        session.Player.OnStateSync(stateSyncs[^1]);
    }
}

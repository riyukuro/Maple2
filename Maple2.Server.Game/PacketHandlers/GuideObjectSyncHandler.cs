using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class GuideObjectSyncHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.GuideObjectSync;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.State != SessionState.Connected || session.Field == null || session.GuideObject == null) {
            return;
        }

        var type = packet.Read<GuideObjectType>();
        if (type != session.GuideObject.Value.Type) {
            return;
        }

        StateSync[]? stateSyncs = ReadRecords(packet);
        if (stateSyncs == null) {
            return;
        }

        using (var buffer = new PoolByteWriter()) {
            buffer.GuideObject(session.GuideObject.ObjectId, stateSyncs);
            session.Field.Broadcast(buffer, sender: session);
        }

        session.GuideObject.Position = stateSyncs[^1].Position;
        session.GuideObject.Rotation = new Vector3(0, 0, stateSyncs[^1].Rotation);
    }

    // After the kind: u8 count, then per record the UserSync record and one i32, the client's tick.
    // Null for no record or for a record the other clients' reader would fail on.
    public static StateSync[]? ReadRecords(IByteReader packet) => ReadRecords(packet, out _);

    // ticks: the client's tick of each record.
    public static StateSync[]? ReadRecords(IByteReader packet, out int[] ticks) {
        ticks = [];
        byte segments = packet.ReadByte();
        if (segments == 0) {
            return null;
        }

        var stateSyncs = new StateSync[segments];
        int[] recordTicks = new int[segments];
        for (int i = 0; i < segments; i++) {
            var playerState = packet.Peek<ActorState>();
            StateSync stateSync = playerState switch {
                ActorState.CoupleEmotion => new StateSyncCoupleEmotion(),
                ActorState.MicroGameRps => new StateSyncRps(),
                ActorState.MicroGameCoupleDance => new StateSyncCoupleDance(),
                ActorState.WeddingEmotion => new StateSyncWeddingEmotion(),
                _ => new StateSync(),
            };

            stateSync.ReadFrom(packet);
            // Same record as UserSync: a record the other clients' reader would fail on is not sent on.
            if (!StateSyncRules.IsRelayable((byte) stateSync.SubState, stateSync.Unknown1)) {
                return null;
            }
            stateSyncs[i] = stateSync;

            recordTicks[i] = packet.ReadInt(); // ClientTicks
        }

        ticks = recordTicks;
        return stateSyncs;
    }
}

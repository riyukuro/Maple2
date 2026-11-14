using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class RideSyncHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RideSync;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.State != SessionState.Connected || session.Field == null) {
            return;
        }
        // The client sends RideSync only from its mount's controller (StateRideeController, state.cpp:536), and the
        // other clients drop one for a player without a mount (CSceneStateGame::OnRideSync, scenestategame.cpp:2478).
        // Without a ride the records would still move the player and set its state to Ride.
        if (session.Ride?.Ride == null) {
            return;
        }

        StateSync[]? stateSyncs = ReadRecords(packet);
        if (stateSyncs == null) {
            return;
        }

        using (var buffer = new PoolByteWriter()) {
            buffer.Ride(session.Player.ObjectId, stateSyncs);
            session.Field?.Broadcast(buffer, sender: session);
        }

        session.Player.OnStateSync(stateSyncs[^1]);
    }

    // The client writes RideSync like UserSync (StateRideeController::vfunc_0): u8, i32 server tick, i32 elapsed,
    // u8 count, then per record the same virtual record writer, so a record of a state with extras (couple
    // emotion, rps, couple dance, wedding emotion) carries them here too, and the client's tick.
    // Null for no record or for a record the other clients' reader would fail on.
    public static StateSync[]? ReadRecords(IByteReader packet) {
        packet.ReadByte(); // Unknown
        packet.ReadInt(); // ServerTicks
        packet.ReadInt(); // ClientTicks

        StateSync[]? stateSyncs = GuideObjectSyncHandler.ReadRecords(packet);
        if (stateSyncs == null) {
            return null;
        }
        foreach (StateSync stateSync in stateSyncs) {
            if (stateSync.State is ActorState.Walk) {
                stateSync.State = ActorState.Ride;
            }
        }
        return stateSyncs;
    }
}

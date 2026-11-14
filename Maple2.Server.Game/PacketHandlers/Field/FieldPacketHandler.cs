using System.Buffers;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers.Field;

// Safe for use for freely modifying state on Field. Handlers are run on Field's thread
public abstract class FieldPacketHandler : PacketHandler<GameSession> {
    protected FieldPacketHandler() { }

    public override bool TryHandleDeferred(GameSession session, IByteReader reader) {
        return Queue(this, session, reader);
    }

    private static bool Queue(FieldPacketHandler handler, GameSession session, IByteReader reader) {
        if (reader is not ByteReader packet) {
            return false;
        }

        if (session.Field is null) {
            return false;
        }

        byte[] bufferCopy = ArrayPool<byte>.Shared.Rent(packet.Length);
        Array.Copy(packet.Buffer, bufferCopy, packet.Length);
        var packetCopy = new FieldPacketReader(bufferCopy, packet.Length, packet.Position);

        session.Field.QueuePacket(handler, session, packetCopy);

        return true;
    }

    // The longest a receive thread waits for a queued move (QueueAndWait): past the save's longest wait on a reset
    // being stored (60 s) and the World calls' deadlines.
    internal static readonly TimeSpan MoveWait = TimeSpan.FromSeconds(120);

    // For a move that saves the session (the channel change, the quit to the character select): queued on the field
    // thread, and the session's receive thread reads no later packet until the field thread has handled it, as when
    // the handler ran on the receive thread. A packet read meanwhile by a handler that runs on the receive thread (a
    // guild fund donation) would else run during or after the move's save, and what it changed is not saved. The wait
    // ends early once the move's save is made (the session's later packets are then dropped, PacketRouter) or the
    // session closes: a Disconnect of the handler joins the receive thread. It also ends when the field it was queued
    // on is disposed (a room whose time ran out) or the session is no longer in it before the packet started: that
    // field never runs it. Once it started, the wait lasts until it is done: a portal, ReturnField or EnterInitField
    // sets the new field in the middle of the handler, while the player is still being moved over.
    protected bool QueueAndWait(GameSession session, IByteReader reader) {
        var awaited = new AwaitedPacket(this);
        FieldManager? field = session.Field;
        if (field == null || !Queue(awaited, session, reader)) {
            return false;
        }
        long end = Environment.TickCount64 + (long) MoveWait.TotalMilliseconds;
        while (!awaited.Done.Wait(20)) {
            if (session.State is SessionState.Disconnected || session.DropsPackets) {
                break;
            }
            // The field is read before Started: a field the packet itself changed is seen with Started set.
            if ((field.Disposed || session.Field != field) && !awaited.Started) {
                break;
            }
            if (Environment.TickCount64 > end) {
                Logger.Warning("{Handler} of {Session}: not handled on the field thread after {Seconds} s, reading on", GetType().Name, session, MoveWait.TotalSeconds);
                break;
            }
        }
        return true;
    }

    // The queued packet of QueueAndWait: handled by its handler, then marks the wait done.
    private sealed class AwaitedPacket : FieldPacketHandler {
        private readonly FieldPacketHandler inner;
        public readonly ManualResetEventSlim Done = new();
        // Set once the field thread began the packet (QueueAndWait then waits for Done whatever the field is).
        private volatile bool started;
        public bool Started => started;

        public AwaitedPacket(FieldPacketHandler inner) {
            this.inner = inner;
        }

        public override RecvOp OpCode => inner.OpCode;

        public override void Handle(GameSession session, IByteReader packet) {
            started = true;
            try {
                inner.Handle(session, packet);
            } finally {
                Done.Set();
            }
        }
    }
}

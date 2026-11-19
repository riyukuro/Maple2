using System.Collections.Concurrent;
using System.Collections.Immutable;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Serilog;

namespace Maple2.Server.Core.Network;

public class PacketRouter<T> where T : Session {
    private readonly ILogger logger = Log.Logger.ForContext<T>();
    private readonly ImmutableDictionary<RecvOp, PacketHandler<T>> handlers;
    // Packets of each op no handler took, all sessions.
    private readonly ConcurrentDictionary<RecvOp, int> unhandledCounts = new();

    public int UnhandledCount(RecvOp op) => unhandledCounts.GetValueOrDefault(op);

    public PacketRouter(IEnumerable<PacketHandler<T>> packetHandlers) {
        var builder = ImmutableDictionary.CreateBuilder<RecvOp, PacketHandler<T>>();
        foreach (PacketHandler<T> packetHandler in packetHandlers.OrderBy(handler => handler.OpCode)) {
            Register(builder, packetHandler);
        }
        handlers = builder.ToImmutable();
    }

    public void OnPacket(object? sender, IByteReader reader) {
        var op = reader.Read<RecvOp>();
        PacketHandler<T>? handler = handlers.GetValueOrDefault(op);
        if (sender is not T session) return;
        // A session that handles no more packets (a game session after its move's save, GameSession.DropsPackets).
        if (session.DropsPackets) return;

        // Let another system schedule when to call Handle
        if (handler?.TryHandleDeferred(session, reader) ?? false) {
            return;
        }

        if (handler == null) {
            // Not handled: the packet is dropped. One line per op per session, with this op's count on this server.
            int count = unhandledCounts.AddOrUpdate(op, 1, (_, n) => n + 1);
            if (session.UnhandledOps.ShouldLog(op)) {
                logger.Warning("Unhandled op {OpCode} ({Count} so far) ({Session}, account {AccountId})",
                    $"0x{(ushort) op:X4}", count, session, session.AccountId);
            }
            return;
        }

        // A handler run here holds the session's gate: a move's save waits for it to end, and the check above is made
        // again under it for a save that ran while this thread waited (Session.InlineGate).
        lock (session.InlineGate) {
            if (session.DropsPackets) return;
            try {
                handler.Handle(session, new BoundedPacketReader(reader, reader.Available));
            } catch (PacketOverreadException ex) {
                // The handler reads more than the client wrote: drop the packet, keep the session.
                if (session.DropLog.ShouldLog(DateTime.UtcNow, out int drops)) {
                    logger.Warning("Dropped packet {OpCode} in {Handler}: read of {Wanted} bytes with {Left} left ({Session}, account {AccountId}, {Drops} dropped since the last line)",
                        $"0x{(ushort) op:X4}", handler.GetType().Name, ex.Wanted, ex.Left, session, session.AccountId, drops);
                }
            }
        }
    }

    private void Register(ImmutableDictionary<RecvOp, PacketHandler<T>>.Builder builder, PacketHandler<T> packetHandler) {
        logger.Debug("Registered [{OpCode}] {Name}", $"0x{(ushort) packetHandler.OpCode:X4}", packetHandler.GetType().Name);
        builder.Add(packetHandler.OpCode, packetHandler);
    }
}

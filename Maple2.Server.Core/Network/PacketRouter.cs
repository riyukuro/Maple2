using System.Collections.Immutable;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Serilog;

namespace Maple2.Server.Core.Network;

public class PacketRouter<T> where T : Session {
    private readonly ILogger logger = Log.Logger.ForContext<T>();
    private readonly ImmutableDictionary<RecvOp, PacketHandler<T>> handlers;

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

        // Let another system schedule when to call Handle
        if (handler?.TryHandleDeferred(session, reader) ?? false) {
            return;
        }

        if (handler == null) {
            return;
        }

        try {
            handler.Handle(session, new BoundedPacketReader(reader, reader.Available));
        } catch (PacketOverreadException ex) {
            // The handler reads more than the client wrote: drop the packet, keep the session.
            logger.Warning("Dropped packet {OpCode} in {Handler}: read of {Wanted} bytes with {Left} left",
                $"0x{(ushort) op:X4}", handler.GetType().Name, ex.Wanted, ex.Left);
        }
    }

    private void Register(ImmutableDictionary<RecvOp, PacketHandler<T>>.Builder builder, PacketHandler<T> packetHandler) {
        logger.Debug("Registered [{OpCode}] {Name}", $"0x{(ushort) packetHandler.OpCode:X4}", packetHandler.GetType().Name);
        builder.Add(packetHandler.OpCode, packetHandler);
    }
}

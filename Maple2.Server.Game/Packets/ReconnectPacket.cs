using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The client's reconnect (CReconnectionManagerCli::OnReconnectPacket, reconnectionmanagercli.cpp): 0 gives the
// reconnect context, 2 the result of a request (1 succeeds, any other number fails), 3 refuses: the client drops its
// reconnect state and, in the game scene, quits to the login scene with message 0x20f.
public static class ReconnectPacket {
    private enum Command : byte {
        Context = 0,
        Result = 2,
        Refuse = 3,
    }

    public static ByteWriter Refuse() {
        var pWriter = Packet.Of(SendOp.Reconnect);
        pWriter.Write<Command>(Command.Refuse);

        return pWriter;
    }
}

// A reconnect request as CReconnectionManagerCli::SendReconnectRequest writes it.
public readonly record struct ReconnectRequest(
    byte Kind,
    long AccountId,
    long CharacterId,
    int Attempt,
    int Sequence,
    string MachineId,
    int Mode,
    string Host,
    int Port) {
    public static ReconnectRequest Read(IByteReader packet) {
        byte kind = packet.ReadByte();
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        int attempt = packet.ReadInt();
        int sequence = packet.ReadInt();
        string machineId = packet.ReadUnicodeString();
        // ReconnectionContext::Write: what the server's Context command gave.
        int mode = packet.ReadInt();
        string host = packet.ReadString();
        int port = packet.ReadInt();
        return new ReconnectRequest(kind, accountId, characterId, attempt, sequence, machineId, mode, host, port);
    }
}

using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Opcode 0x137 (TermTagManagerCli::OnPacket). A running term is registered in the client's term tags
// (CGameRule::HasTermTag); an ended one moves to the finished list. Entry: TermTagContext read.
public static class TermTagPacket {
    private enum Command : byte {
        Load = 0,
        Start = 1,
        End = 2,
    }

    public static ByteWriter Load(ICollection<TermTagTable.Entry> finished, ICollection<TermTagTable.Entry> running) {
        var pWriter = Packet.Of(SendOp.TermTag);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteInt(finished.Count);
        pWriter.WriteInt(running.Count);
        foreach (TermTagTable.Entry entry in finished) {
            WriteEntry(pWriter, entry);
        }
        foreach (TermTagTable.Entry entry in running) {
            WriteEntry(pWriter, entry);
        }
        return pWriter;
    }

    public static ByteWriter Start(ICollection<TermTagTable.Entry> entries) => List(Command.Start, entries);

    public static ByteWriter End(ICollection<TermTagTable.Entry> entries) => List(Command.End, entries);

    private static ByteWriter List(Command command, ICollection<TermTagTable.Entry> entries) {
        var pWriter = Packet.Of(SendOp.TermTag);
        pWriter.WriteByte((byte) command);
        pWriter.WriteInt(entries.Count);
        foreach (TermTagTable.Entry entry in entries) {
            WriteEntry(pWriter, entry);
        }
        return pWriter;
    }

    // The trailing bool is 0 in every KMS2 entry seen; its use is not known.
    private static void WriteEntry(ByteWriter pWriter, TermTagTable.Entry entry) {
        pWriter.WriteInt(entry.Id);
        pWriter.WriteUnicodeString(entry.Name);
        pWriter.WriteLong(entry.Start);
        pWriter.WriteLong(entry.End);
        pWriter.WriteBool(false);
    }
}

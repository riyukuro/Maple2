using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

public static class BreakablePacket {
    private enum Command : byte {
        BatchUpdate = 0,
        Update = 1,
    }

    public static ByteWriter Update(ICollection<FieldBreakable> breakables) {
        var pWriter = Packet.Of(SendOp.Breakable);
        pWriter.WriteByte((byte) Command.BatchUpdate);

        pWriter.WriteInt(breakables.Count);
        foreach (FieldBreakable breakable in breakables) {
            WriteBreakable(pWriter, breakable);
        }

        return pWriter;
    }

    public static ByteWriter Update(FieldBreakable breakable) {
        var pWriter = Packet.Of(SendOp.Breakable);
        pWriter.WriteByte((byte) Command.Update);
        WriteBreakable(pWriter, breakable);

        return pWriter;
    }

    // name, state, the move flag, a time added to the move cycle and the cycle point a stopped one holds
    // (CSceneStateGame::OnBreakable, scenestategame.cpp:3667; CBreakableObject::Update, breakableobject.cpp:382-392).
    // The added time is 0: the cycle runs on the server tick the client already has.
    private static void WriteBreakable(ByteWriter pWriter, FieldBreakable breakable) {
        pWriter.WriteString(breakable.EntityId);
        pWriter.WriteByte((byte) breakable.State);
        pWriter.WriteBool(breakable.Moving);
        pWriter.WriteInt(0);
        pWriter.WriteInt(breakable.FreezeTick);
    }
}

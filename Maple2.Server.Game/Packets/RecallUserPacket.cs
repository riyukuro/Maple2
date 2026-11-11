using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Client: CSceneStateGame::OnRecallUser (scenestategame.cpp:4733).
public static class RecallUserPacket {
    private enum Command : byte {
        Invite = 4,
        Answer = 5,
        Notice = 6,
        Fail = 7,
    }

    // The result byte the client itself sends in c2s 7 when an invite arrives while its confirm box is open.
    public const byte FAIL_BUSY = 4;
    // Any value the client has no own message for (2, 4 and 6 have one) shows the default message.
    public const byte FAIL_DEFAULT = 0;

    public const byte ANSWER_ACCEPT = 0;
    public const byte ANSWER_DECLINE = 1;

    // Kind 4: byte type (read, not used), the caller's name, int invite id. The client opens a confirm box with a
    // 20 second timer and answers with c2s 4 (invite id, byte accept).
    public static ByteWriter Invite(string callerName, int inviteId, byte type = 0) {
        var pWriter = Packet.Of(SendOp.RecallUser);
        pWriter.WriteByte((byte) Command.Invite);
        pWriter.WriteByte(type);
        pWriter.WriteUnicodeString(callerName);
        pWriter.WriteInt(inviteId);

        return pWriter;
    }

    // Kind 5: name, byte result. 0 and 2 show one system message with the name, 1 and 3 another.
    public static ByteWriter Answer(string name, byte result) {
        var pWriter = Packet.Of(SendOp.RecallUser);
        pWriter.WriteByte((byte) Command.Answer);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteByte(result);

        return pWriter;
    }

    // Kind 6: name; one system message with the name.
    public static ByteWriter Notice(string name) {
        var pWriter = Packet.Of(SendOp.RecallUser);
        pWriter.WriteByte((byte) Command.Notice);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // Kind 7: name, byte result. 2, 4 and 6 each show their own system message, every other value the default one.
    public static ByteWriter Fail(string name, byte result) {
        var pWriter = Packet.Of(SendOp.RecallUser);
        pWriter.WriteByte((byte) Command.Fail);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteByte(result);

        return pWriter;
    }
}

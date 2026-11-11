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

    // Kind 7 results, by the string the client shows (commonfiles/commonstringtable.cpp, ids 2302 to 2305).
    public const byte FAIL_REJECT = 0; // every value but 2, 4 and 6: s_recall_user_notify_reject
    public const byte FAIL_CANNOT_MOVE = 2; // s_recall_user_notify_cannot_move
    // s_recall_user_notify_already_recalled; also the byte the client sends in c2s 7 when an invite arrives while
    // its confirm box is open.
    public const byte FAIL_BUSY = 4;
    public const byte FAIL_NOT_FOUND = 6; // s_recall_user_notify_not_found

    // Kind 5 results: 0 and 2 show s_recall_user_notify_recall (the player is recalled without a question),
    // 1 and 3 show s_recall_user_notify_confirm_recall (the player is being asked).
    public const byte REQUEST_RECALL = 0;
    public const byte REQUEST_CONFIRM = 1;

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

    // Kind 5: name, byte result. Tells the caller that the request went out (REQUEST_RECALL or REQUEST_CONFIRM).
    public static ByteWriter Requested(string name, byte result) {
        var pWriter = Packet.Of(SendOp.RecallUser);
        pWriter.WriteByte((byte) Command.Answer);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteByte(result);

        return pWriter;
    }

    // Kind 6: name; shows s_recall_user_notify_recalled (the recall was done).
    public static ByteWriter Recalled(string name) {
        var pWriter = Packet.Of(SendOp.RecallUser);
        pWriter.WriteByte((byte) Command.Notice);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // The packet that tells the caller how an invite ended: kind 6 when the player was moved, else kind 7.
    public static ByteWriter Result(string name, bool recalled, byte fail) {
        return recalled ? Recalled(name) : Fail(name, fail);
    }

    // Kind 7: name, byte result (a FAIL_ value).
    public static ByteWriter Fail(string name, byte result) {
        var pWriter = Packet.Of(SendOp.RecallUser);
        pWriter.WriteByte((byte) Command.Fail);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteByte(result);

        return pWriter;
    }
}

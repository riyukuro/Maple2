using System.Web;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class ChatPacket {
    // The byte after the chat type: the sender is a GM. Every receiver but the sender then sees the name and
    // the text in the client's GM chat colour (white by default). It is set from the sender's account.
    // channel: see UserChatHandler.LineChannel.
    public static ByteWriter Message(Player player, ChatType type, string message, int channel = 0) {
        var pWriter = Packet.Of(SendOp.UserChat);
        pWriter.WriteLong(player.Account.Id);
        pWriter.WriteLong(player.Character.Id);
        pWriter.WriteUnicodeString(player.Character.Name);
        pWriter.WriteBool(false);
        pWriter.WriteUnicodeString(message);
        pWriter.Write<ChatType>(type);
        pWriter.WriteBool(AdminLevel.IsGm(player.Account.AdminPermissions));
        pWriter.WriteInt(channel);

        switch (type) {
            case ChatType.WhisperFrom:
                pWriter.WriteUnicodeString();
                break;
            case ChatType.Super:
                pWriter.WriteInt();
                break;
            case ChatType.Club:
                pWriter.WriteLong();
                break;
        }

        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter Message(long accountId, long characterId, string characterName, ChatType type, string message, int superChatId = 0, long clubId = 0, bool gm = false, int channel = 0) {
        var pWriter = Packet.Of(SendOp.UserChat);
        pWriter.WriteLong(accountId);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(characterName);
        pWriter.WriteBool(false);
        pWriter.WriteUnicodeString(message);
        pWriter.Write<ChatType>(type);
        pWriter.WriteBool(gm);
        // The sender's channel. The client prints it in front of the world and super chat lines. In the lines of a
        // player (normal, party, guild, whisper) it is 0 when the sender stands in an instanced field.
        pWriter.WriteInt(channel);

        switch (type) {
            case ChatType.WhisperFrom:
                pWriter.WriteUnicodeString();
                break;
            case ChatType.Super:
                pWriter.WriteInt(superChatId);
                break;
            case ChatType.Club:
                pWriter.WriteLong(clubId);
                break;
        }

        pWriter.WriteBool(false);

        return pWriter;
    }

    // picture: the sender's profile picture, for the portrait of the whisper notice. Null makes the sender's own copy.
    public static ByteWriter Whisper(long accountId, long characterId, string name, string message, string? picture = null, bool gm = false, int channel = 0) {
        ChatType type = picture == null ? ChatType.WhisperTo : ChatType.WhisperFrom;

        var pWriter = Packet.Of(SendOp.UserChat);
        pWriter.WriteLong(accountId);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteBool(false);
        pWriter.WriteUnicodeString(message);
        pWriter.Write<ChatType>(type);
        // Only the received line: the sender's own copy carries the recipient's name and stays normal.
        pWriter.WriteBool(gm && type == ChatType.WhisperFrom);
        // The sender's channel in the received line; the own copy keeps 0.
        pWriter.WriteInt(type == ChatType.WhisperFrom ? channel : 0);

        if (type == ChatType.WhisperFrom) {
            pWriter.WriteUnicodeString(picture ?? string.Empty);
        }

        pWriter.WriteBool(false);

        return pWriter;
    }

    // The notice that a player refuses whispers. The client prints string 0x8cd with the name (UIMessengerBrowser
    // vfunc_84). KMS2 sends the refusing player's ids, name and channel, and the text as common string 0: the client
    // drops a line whose text is empty before it looks at the type.
    public static ByteWriter WhisperReject(long accountId, long characterId, string name, int channel) {
        var pWriter = Packet.Of(SendOp.UserChat);
        pWriter.WriteLong(accountId);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteBool(true);
        pWriter.WriteInt(0);
        pWriter.Write<ChatType>(ChatType.WhisperReject);
        pWriter.WriteBool(false);
        pWriter.WriteInt(channel);
        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter System(string type, string message) {
        var pWriter = Packet.Of(SendOp.UserChat);
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteUnicodeString(type);
        pWriter.WriteBool(false);
        pWriter.WriteUnicodeString(message);
        pWriter.Write<ChatType>(ChatType.System);
        pWriter.WriteBool(false);
        pWriter.WriteInt();
        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter Alert(StringCode code) {
        var pWriter = Packet.Of(SendOp.UserChat);
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteUnicodeString();
        pWriter.WriteBool(true);
        pWriter.WriteInt(StringCodeMap.ToClient(code)); // KMS 2525: client string ids differ from the server enum
        pWriter.Write<ChatType>(ChatType.NoticeAlert);
        pWriter.WriteBool(false);
        pWriter.WriteInt();
        pWriter.WriteBool(false);

        return pWriter;
    }

    public static ByteWriter Alert(string message, bool htmlEncoded = false) {
        var pWriter = Packet.Of(SendOp.UserChat);
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteUnicodeString();
        pWriter.WriteBool(false);
        pWriter.WriteUnicodeString(htmlEncoded ? message : HttpUtility.HtmlEncode(message));
        pWriter.Write<ChatType>(ChatType.NoticeAlert);
        pWriter.WriteBool(false);
        pWriter.WriteInt();
        pWriter.WriteBool(false);

        return pWriter;
    }
}

using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// GroupChat 0x004C: 1 name; 2 name, i32 chat id; 4 i32 chat id, u8 refused; 10 text, i32 chat id.
// The client sends no name above 24 letters and no text above 2048.
public static class GroupChatRules {
    public const int MaxNameLength = 24;
    public const int MaxTextLength = 2048;

    public static (string Name, int ChatId) ReadInvite(IByteReader packet) {
        string name = packet.ReadUnicodeString();
        return (name, packet.ReadInt());
    }

    public static (int ChatId, bool Refused) ReadLeave(IByteReader packet) {
        int chatId = packet.ReadInt();
        return (chatId, packet.ReadBool());
    }

    public static (string Text, int ChatId) ReadChat(IByteReader packet) {
        string text = packet.ReadUnicodeString();
        return (text, packet.ReadInt());
    }

    public static bool NameValid(string name) {
        return name.Length <= MaxNameLength;
    }

    public static bool TextValid(string text) {
        return text.Length is > 0 and <= MaxTextLength;
    }
}

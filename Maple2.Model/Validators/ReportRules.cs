using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// RequestReport 0x0052: target name, text, u8 kind, i32 flags, then the kind's own fields
// (1 chat line; 2 i64 poster id, picture address; 3 i64 item; 4 i64 home, i32 map, i32 plot; 7 pet name).
// The client sends no text above 4000 letters and none that is empty.
public static class ReportRules {
    public const int MaxTextLength = 4000;

    public static (string Target, string Text, byte Kind, int Flags) ReadHead(IByteReader packet) {
        string target = packet.ReadUnicodeString();
        string text = packet.ReadUnicodeString();
        byte kind = packet.ReadByte();
        return (target, text, kind, packet.ReadInt());
    }

    public static bool TextValid(string text) {
        return text.Length is > 0 and <= MaxTextLength;
    }

    // The chat line, picture address or pet name stored with the report: no longer than the text may be.
    public static bool AttachedValid(string attached) {
        return attached.Length <= MaxTextLength;
    }
}

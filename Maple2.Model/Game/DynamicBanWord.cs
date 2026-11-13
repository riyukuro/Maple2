using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

// A word of the live ban-word list (DynamicBanword 0x124), the client's BannedWordData. Type 1 (DynamicBanWordRules.Mask)
// replaces the word in a chat line with Replacement, or with one '*' per letter when that is empty; any other type
// (Block) drops the line.
public class DynamicBanWord : IByteSerializable {
    public int Id { get; init; }
    public int Type { get; init; }
    public string Word { get; init; } = string.Empty;
    public string Replacement { get; init; } = string.Empty;

    // BannedWordData::Read (0x140348f30): id, type, word, replacement.
    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteInt(Type);
        writer.WriteUnicodeString(Word);
        writer.WriteUnicodeString(Replacement);
    }
}

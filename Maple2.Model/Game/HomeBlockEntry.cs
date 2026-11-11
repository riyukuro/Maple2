using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

// A player blocked from an account's home (the client's HomeBlockInfo).
public class HomeBlockEntry : IByteSerializable {
    public long Id { get; init; }
    public long CharacterId { get; init; }
    public long AccountId { get; init; }
    public string Name { get; init; } = string.Empty;
    public short Level { get; init; }
    public Job Job { get; init; }
    public string Picture { get; init; } = string.Empty;
    public string Memo { get; set; } = string.Empty;

    // HomeBlockInfo::Read.
    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(Id);
        writer.WriteLong(CharacterId);
        writer.WriteLong(AccountId);
        writer.WriteUnicodeString(Name);
        writer.WriteShort(Level);
        writer.WriteInt((int) Job.Code());
        writer.WriteInt((int) Job);
        writer.WriteUnicodeString(Picture);
        writer.WriteUnicodeString(Memo);
    }
}

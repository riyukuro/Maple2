using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

// An account restriction (AdminBlock 0x62), the client's AccountBlockInfo. Times are epoch seconds.
public class AccountBlock : IByteSerializable {
    public long Id { get; init; }
    public long AccountId { get; init; }
    public int Kind { get; init; }
    public string Reason { get; init; } = string.Empty;
    public long StartTime { get; init; }
    public long ExpiryTime { get; set; }

    // AccountBlockInfo::Read (0x140268750): id, kind, a long the client keeps but never reads (Chosen here: the
    // account id), reason, start, expiry.
    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(Id);
        writer.WriteInt(Kind);
        writer.WriteLong(AccountId);
        writer.WriteUnicodeString(Reason);
        writer.WriteLong(StartTime);
        writer.WriteLong(ExpiryTime);
    }
}

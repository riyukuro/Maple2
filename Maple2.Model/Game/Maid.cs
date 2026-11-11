using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

// A maid placed in a home: its furnishing cube's uid is the maid's id (the key of the client's maid managers).
// The packet layout is CMaid::ReadFromPacket (commonfiles/maid.cpp) after the manager's mode byte.
public class Maid : IByteSerializable {
    public required long Uid { get; init; }
    public required long OwnerId { get; init; } // the home's account
    public required int ItemId { get; init; }
    public required int MaidId { get; init; }
    public int Mood { get; set; }
    public int Level { get; set; } = 1; // the affinity level; a new CMaid starts at 1
    public int Exp { get; set; }
    public required long CreationTime { get; init; }
    public required long ExpiryTime { get; set; } // the contract end; the client shows the maid as expired after it
    public MaidProfile Profile { get; set; } = new();

    public void WriteTo(IByteWriter writer) {
        writer.WriteByte(); // the maid's mode (CMaid::SetMode)
        writer.WriteLong(); // unknown, 0
        writer.WriteLong(Uid);
        // Two times a new CMaid sets to the server time (IsTime38Over, IsTime40Over).
        writer.WriteLong(CreationTime);
        writer.WriteLong(CreationTime);
        writer.WriteLong(); // unknown, 0
        writer.WriteInt(ItemId);
        writer.WriteInt(MaidId);
        writer.WriteByte(1); // a new CMaid sets it to 1
        writer.WriteInt(Mood);
        writer.WriteInt(Level);
        writer.WriteInt(Exp);
        writer.WriteLong(ExpiryTime);
        writer.WriteInt(); // a list of ids, each sent as a long; empty
        writer.WriteClass<MaidProfile>(Profile);
    }
}

// The 13 profile texts of a maid (MaidProfile::Read). The client lets the owner fill them in while the name is empty.
public class MaidProfile : IByteSerializable {
    public string Title { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BirthDay { get; set; } = string.Empty;
    public string Constellation { get; set; } = string.Empty;
    public string Height { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string SpecialDetail { get; set; } = string.Empty;
    public string Like { get; set; } = string.Empty;
    public string Hate { get; set; } = string.Empty;
    public string Hobby { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string BelongTo { get; set; } = string.Empty;

    public void WriteTo(IByteWriter writer) {
        writer.WriteUnicodeString(Title);
        writer.WriteUnicodeString(Name);
        writer.WriteUnicodeString(BirthDay);
        writer.WriteUnicodeString(Constellation);
        writer.WriteUnicodeString(Height);
        writer.WriteUnicodeString(Weight);
        writer.WriteUnicodeString(Specialty);
        writer.WriteUnicodeString(SpecialDetail);
        writer.WriteUnicodeString(Like);
        writer.WriteUnicodeString(Hate);
        writer.WriteUnicodeString(Hobby);
        writer.WriteUnicodeString(OwnerName);
        writer.WriteUnicodeString(BelongTo);
    }
}

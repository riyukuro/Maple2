using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

public sealed class ItemCustomMusicScore : IByteSerializable, IByteDeserializable {
    public int Length;
    public Instrument Instrument;
    public string Title;
    public string Author;
    public long AuthorId; // AccountId
    public bool IsLocked; // true=>s_writemusic_error_cant_edit
    public string Mml;

    // The owner type of the author id (the client matches it with the account owner: FUN_1406a5a50, ViewScore).
    public const int AccountAuthorType = 1;

    public ItemCustomMusicScore() {
        Title = string.Empty;
        Author = string.Empty;
        Mml = string.Empty;
    }

    public ItemCustomMusicScore Clone() {
        return (ItemCustomMusicScore) MemberwiseClone();
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Length);
        writer.Write<Instrument>(Instrument);
        writer.WriteUnicodeString(Title);
        writer.WriteUnicodeString(Author);
        writer.WriteInt(AccountAuthorType);
        writer.WriteLong(AuthorId);
        writer.WriteBool(IsLocked);
        writer.WriteLong();
        writer.WriteLong();
    }

    public void ReadFrom(IByteReader reader) {
        Length = reader.ReadInt();
        Instrument = reader.Read<Instrument>();
        Title = reader.ReadUnicodeString();
        Author = reader.ReadUnicodeString();
        reader.ReadInt();
        AuthorId = reader.ReadLong();
        IsLocked = reader.ReadBool();
        reader.ReadLong();
        reader.ReadLong();
    }
}

using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

// The music score of a UGC market listing, as the client's CUgcItemMusicNote reads it (ugcdef.cpp:1727): the listed
// score item's uid (FindProductByMusicUid, IsAlreadyListed), its length, instrument, author and title (the market's
// music panel: title m_str30, writer m_str28, uimeratmarketdialog.cpp:5328), the notes, and the author block the
// client's score item carries (owner type and account id). A bought copy is made from it (SetFromScoreInfo).
public class UgcItemMusicScore : IByteSerializable, IByteDeserializable {
    public long ItemUid;
    public int Length;
    public int Instrument;
    public string Author = string.Empty;
    public string Title = string.Empty;
    public string Mml = string.Empty;
    public int AuthorType;
    public long AuthorId;

    public static UgcItemMusicScore From(long itemUid, ItemCustomMusicScore score) {
        return new UgcItemMusicScore {
            ItemUid = itemUid,
            Length = score.Length,
            Instrument = (int) score.Instrument,
            Author = score.Author,
            Title = score.Title,
            Mml = score.Mml,
            AuthorType = ItemCustomMusicScore.AccountAuthorType,
            AuthorId = score.AuthorId,
        };
    }

    // The buyer's copy (CItemCustomMusicScore::SetFromScoreInfo, itemsocketsystemutil.cpp:1816): the listing's score,
    // locked (m_n38 = 1), so the buyer can neither edit nor list it.
    public ItemCustomMusicScore ToBoughtCopy() {
        return new ItemCustomMusicScore {
            Length = Length,
            Instrument = (Enum.Instrument) Instrument,
            Title = Title,
            Author = Author,
            AuthorId = AuthorId,
            IsLocked = true,
            Mml = Mml,
        };
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(ItemUid);
        writer.WriteInt(Length);
        writer.WriteInt(Instrument);
        writer.WriteUnicodeString(Author);
        writer.WriteUnicodeString(Title);
        writer.WriteString(Mml);
        writer.WriteInt(AuthorType);
        writer.WriteLong(AuthorId);
        writer.WriteLong();
        writer.WriteUnicodeString();
    }

    public void ReadFrom(IByteReader reader) {
        ItemUid = reader.ReadLong();
        Length = reader.ReadInt();
        Instrument = reader.ReadInt();
        Author = reader.ReadUnicodeString();
        Title = reader.ReadUnicodeString();
        Mml = reader.ReadString();
        AuthorType = reader.ReadInt();
        AuthorId = reader.ReadLong();
        reader.ReadLong();
        reader.ReadUnicodeString();
    }
}

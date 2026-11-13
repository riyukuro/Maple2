using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class HeldCube : IByteSerializable, IByteDeserializable {
    public static readonly HeldCube Default = new();

    public long Id { get; set; }
    public int ItemId { get; protected set; }
    public ItemType ItemType { get; protected set; }

    public UgcItemLook? Template { get; protected set; }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(ItemId);
        writer.WriteLong(Id);
        // The client's cube record keeps this long as the cube's expiry time: the build palette fills the same field
        // from a bag item's expiry (CItem's 4th field, Item.ExpiryTime). A placed cube keeps no expiry here, so 0 is
        // sent and the client has no end time for a timed furnishing once it is placed (see FORK.md, Cube).
        writer.WriteLong();

        writer.WriteBool(Template != null);
        if (Template != null) {
            writer.WriteClass<UgcItemLook>(Template);
        }
    }

    public void ReadFrom(IByteReader reader) {
        ItemId = reader.ReadInt();
        Id = reader.ReadLong();
        reader.ReadLong();

        bool hasTemplate = reader.ReadBool();
        if (hasTemplate) {
            Template = reader.ReadClass<UgcItemLook>();
        }
    }
}

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
    // The cube's expiry time (epoch seconds, 0 for none): a placed cube takes it from the storage item it was placed
    // from (Item.ExpiryTime), and keeps it in its row.
    public long ExpiryTime { get; set; }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(ItemId);
        writer.WriteLong(Id);
        // The client's cube record keeps this long as the cube's expiry time: the build palette fills the same field
        // from a bag item's expiry (ugccubepalette.cpp:798, CItem's 4th field, Item.ExpiryTime).
        writer.WriteLong(ExpiryTime);

        writer.WriteBool(Template != null);
        if (Template != null) {
            writer.WriteClass<UgcItemLook>(Template);
        }
    }

    public void ReadFrom(IByteReader reader) {
        ItemId = reader.ReadInt();
        Id = reader.ReadLong();
        reader.ReadLong(); // the expiry the client holds: the server keeps its own

        bool hasTemplate = reader.ReadBool();
        if (hasTemplate) {
            Template = reader.ReadClass<UgcItemLook>();
        }
    }
}

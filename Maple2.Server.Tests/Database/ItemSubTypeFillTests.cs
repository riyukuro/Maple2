using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Database;

// The client reads an item's ugc block by the item's data (a cube texture mesh, or type 22; CItem::ReadFromStream,
// commonfiles/item.cpp), the server writes the one the item holds. A row stored without its sub type (a row made
// behind the server) was written without the block, so the client read the next fields as the block and ran past
// the end (FurnishingStorage 3: sock_exception, Decode8). Loading the row fills the block its data asks for.
public class ItemSubTypeFillTests {
    private const int UgcItem = 50200999, PlainItem = 50200998;

    private static byte[] Bytes(Item item) {
        var writer = new ByteWriter();
        writer.WriteClass<Item>(item);
        return writer.ToArray();
    }

    private static Maple2.Database.Model.Item Row(Item item, bool dropSubType) {
        Maple2.Database.Model.Item row = item!;
        if (dropSubType) {
            row.SubType = null;
        }
        return row;
    }

    [Test]
    public void UgcRowWithoutSubType_IsWrittenWithTheUgcBlock() {
        ItemMetadata metadata = TestGameSession.Metadata(UgcItem, type: 22);
        var made = new Item(metadata);
        Assume.That(made.Template, Is.Not.Null);

        Item loaded = Row(made, dropSubType: true).Convert(metadata);

        Assert.That(loaded.Template, Is.Not.Null);
        Assert.That(loaded.Blueprint, Is.Not.Null);
        Assert.That(Bytes(loaded), Is.EqualTo(Bytes(made)));
    }

    // A row that holds its block keeps it (the design's url is not replaced by an empty one).
    [Test]
    public void UgcRowWithSubType_KeepsIt() {
        ItemMetadata metadata = TestGameSession.Metadata(UgcItem, type: 22);
        var made = new Item(metadata);
        made.Template!.Url = "design";

        Item loaded = Row(made, dropSubType: false).Convert(metadata);

        Assert.That(loaded.Template!.Url, Is.EqualTo("design"));
        Assert.That(Bytes(loaded), Is.EqualTo(Bytes(made)));
    }

    // An item whose data asks for no block gets none.
    [Test]
    public void PlainRow_GetsNoBlock() {
        ItemMetadata metadata = TestGameSession.Metadata(PlainItem);
        var made = new Item(metadata);

        Item loaded = Row(made, dropSubType: true).Convert(metadata);

        Assert.That(new object?[] { loaded.Template, loaded.Blueprint, loaded.Pet, loaded.Music, loaded.Badge }.All(block => block == null), Is.True);
        Assert.That(Bytes(loaded), Is.EqualTo(Bytes(made)));
    }
}

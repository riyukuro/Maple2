using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Database;

// The client reads a cap's, hair's or decal's extra floats by the item's own data (CItemData::CreateExtraData,
// commonfiles/itemdata.cpp:1430-1451). A cap row stored with the plain color appearance (a row written outside the
// server) was written without the floats, so the client misread the rest of the bag list and showed none of that tab's
// items. Loading such a row gives it its item's appearance class, keeping the color.
public class ItemAppearanceLoadTests {
    private const int Cap = 11300035, Earring = 11200009;

    private static byte[] Bytes(Item item) {
        var writer = new ByteWriter();
        writer.WriteClass<Item>(item);
        return writer.ToArray();
    }

    private static Item Load(ItemMetadata metadata, ItemAppearance stored) {
        var made = new Item(metadata) { Appearance = stored };
        Maple2.Database.Model.Item row = made!;
        return row.Convert(metadata);
    }

    [Test]
    public void CapRowWithAPlainAppearance_LoadsAsACap() {
        ItemMetadata metadata = TestGameSession.Metadata(Cap, EquipSlot.CP);
        var color = new EquipColor(new Color(1, 2, 3, 4), new Color(5, 6, 7, 8), new Color(9, 10, 11, 12), 3, 2);

        Item loaded = Load(metadata, new ItemAppearance(color));

        Assert.That(loaded.Appearance, Is.TypeOf<CapAppearance>());
        Assert.That(loaded.Appearance!.Color, Is.EqualTo(color));
        Assert.That(Bytes(loaded).Length, Is.EqualTo(Bytes(new Item(metadata)).Length));
    }

    // A row of the right class keeps its appearance (a cap keeps its positions); a plain item stays plain.
    [Test]
    public void RowOfItsItemsClass_KeepsIt() {
        ItemMetadata cap = TestGameSession.Metadata(Cap, EquipSlot.CP);
        var stored = new CapAppearance(EquipColor.None, new System.Numerics.Vector3(1, 2, 3), unknown: 0.5f);
        Assert.That(Item.LoadedAppearance(stored, cap), Is.SameAs(stored));

        ItemMetadata earring = TestGameSession.Metadata(Earring, EquipSlot.EA);
        var plain = new ItemAppearance(EquipColor.None);
        Assert.That(Item.LoadedAppearance(plain, earring), Is.SameAs(plain));
        Assert.That(Load(earring, plain).Appearance, Is.TypeOf<ItemAppearance>());
    }
}

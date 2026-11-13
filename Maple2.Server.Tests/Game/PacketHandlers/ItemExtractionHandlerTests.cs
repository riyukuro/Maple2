using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The glamour anvil (ItemExtractionScroll). The result takes the source's appearance, as the client's preview copies
// it (uiitemextractionscrolldialog.cpp:353), and every refusal gets a reply, after which the dialog leaves its
// waiting state (vfunc_67, :273).
public class ItemExtractionHandlerTests {
    private const int SourceId = 11400001;
    private const int ResultId = 11400002;
    private const int AnvilId = 30000001;
    private static readonly EquipColor Dye = new(new Color(1, 2, 3, 255), new Color(4, 5, 6, 255), new Color(7, 8, 9, 255), 11, 3);

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item source = null!;
    private Item anvil = null!;

    private void Start(bool row = true, ItemTag anvilTag = ItemTag.ItemExtraction) {
        test = new TestGameSession();
        ItemMetadata sourceData = TestGameSession.Metadata(SourceId, slot: EquipSlot.CL, type: 1, slotMax: 1);
        ItemMetadata resultData = TestGameSession.Metadata(ResultId, slot: EquipSlot.CL, type: 1, slotMax: 1);
        ItemMetadata anvilData = TestGameSession.Metadata(AnvilId);
        anvilData = anvilData with { Property = anvilData.Property with { Tag = anvilTag } };
        test.Register(sourceData, resultData, anvilData);
        var entries = new Dictionary<int, ItemExtractionTable.Entry>();
        if (row) {
            entries[SourceId] = new ItemExtractionTable.Entry(SourceId, 1, 1, ResultId);
        }
        test.SetTable(new ItemExtractionTable(entries));
        Item storedSource = test.Store(sourceData);
        Item storedAnvil = test.Store(anvilData);
        test.Start();
        source = session.Item.Inventory.Get(storedSource.Uid)!;
        anvil = session.Item.Inventory.Get(storedAnvil.Uid)!;
        source.GlamorForges = 1;
        source.Appearance = new ItemAppearance(Dye);
        test.TakeSent();
    }

    private void Extract() {
        new ItemExtractionHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(anvil.Uid);
            writer.WriteLong(source.Uid);
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // Before, the result kept the fresh colour CreateItem gave it.
    [Test]
    public void Result_TakesTheSourcesDye() {
        Start();
        Extract();

        Item result = session.Item.Inventory.Find(ResultId).Single();
        Assert.Multiple(() => {
            Assert.That(result.Appearance, Is.Not.SameAs(source.Appearance));
            Assert.That(result.Appearance!.Color.Primary, Is.EqualTo(Dye.Primary));
            Assert.That(result.Appearance.Color.Tertiary, Is.EqualTo(Dye.Tertiary));
            Assert.That(result.Appearance.Color.Index, Is.EqualTo(Dye.Index));
            Assert.That(source.GlamorForges, Is.EqualTo(0));
        });
    }

    // Before, these requests got no reply and the dialog stayed waiting.
    [Test]
    public void NoRow_IsAnswered() {
        Start(row: false);
        Extract();
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemExtractionPacket.Refused()) }));
    }

    [Test]
    public void SpentSource_IsAnswered() {
        Start();
        source.GlamorForges = 0;
        Extract();
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemExtractionPacket.Refused()) }));
    }

    [Test]
    public void NotAnAnvil_IsAnsweredAsLackingAnvils() {
        Start(anvilTag: ItemTag.None);
        Extract();
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemExtractionPacket.InsufficientAnvils()) }));
    }
}

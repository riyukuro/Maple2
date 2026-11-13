using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.Packets;

// RequestItemBox, against the client's writer (uiitemmultiopenboxdialog.cpp:137-141): int box id, short rarity,
// int count, unicode string of the picked rows.
public class ItemBoxRequestTests {
    private static ByteReader Request(int itemId, short rarity, int count, string selection) {
        var writer = new ByteWriter();
        writer.WriteInt(itemId);
        writer.WriteShort(rarity);
        writer.WriteInt(count);
        writer.WriteUnicodeString(selection);
        return new ByteReader(writer.Buffer[..writer.Length], 0);
    }

    [Test]
    public void ReadsRarityAndSelection() {
        ByteReader packet = Request(20302280, 4, 10, "1,2");
        (int itemId, short rarity, int count, string selection) = ItemBoxHandler.ReadRequest(packet);
        Assert.That(itemId, Is.EqualTo(20302280));
        Assert.That(rarity, Is.EqualTo((short) 4));
        Assert.That(count, Is.EqualTo(10));
        Assert.That(selection, Is.EqualTo("1,2"));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [Test]
    public void EmptySelectionReadsToEnd() {
        ByteReader packet = Request(20302280, 1, 2, "");
        (_, short rarity, _, string selection) = ItemBoxHandler.ReadRequest(packet);
        Assert.That(rarity, Is.EqualTo((short) 1));
        Assert.That(selection, Is.Empty);
        Assert.That(packet.Available, Is.EqualTo(0));
    }
}

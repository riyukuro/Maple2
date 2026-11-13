using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ItemSocketSystem mode 1 against the client's reader (FUN_140629140, commonfiles/itemsocketsystemutil.cpp:895): byte
// success, long item uid; on success byte unlockSocketIndex, then for the staged item the socket block (byte max,
// byte unlocked, per open slot bool + gem) and bool transfer (+ the transfer block when set).
public class ItemSocketUnlockTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static Item ItemOf(long uid) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 4) { Uid = uid };
    }

    // A 3-socket item whose second socket was just unlocked: the index is that socket's row, 1, not the max (3).
    [Test]
    public void TheIndexIsTheSocketJustUnlocked() {
        Item item = ItemOf(0x0102030405060708L);
        item.Socket = new ItemSocket(3, 2);
        item.Transfer = null;

        Assert.That(Body(ItemSocketPacket.UnlockSocket(true, item)), Is.EqualTo(new byte[] {
            0x01,
            0x01,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0x01,
            0x03, 0x02, 0x00, 0x00,
            0x00,
        }));
    }

    [Test]
    public void TheFirstUnlockIsRowZero() {
        Item item = ItemOf(1);
        item.Socket = new ItemSocket(3, 1);
        item.Transfer = null;

        byte[] body = Body(ItemSocketPacket.UnlockSocket(true, item));
        Assert.That(body[10], Is.EqualTo(0x00));
        Assert.That(body.Length, Is.EqualTo(15));
    }
}

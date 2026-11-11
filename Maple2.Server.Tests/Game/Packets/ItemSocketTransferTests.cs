using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

public class ItemSocketTransferTests {
    private static Item ItemOf(long uid) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 4) { Uid = uid };
    }

    // Client: FUN_1406295b0 (commonfiles/itemsocketsystemutil.cpp:933): long source uid, socket block (byte max,
    // byte unlocked, per slot bool + gem), long target uid, socket block, bool, transfer block when set.
    [Test]
    public void TransferSocketWithoutTransferBlock() {
        Item source = ItemOf(91);
        source.Socket = new ItemSocket(3, 1);
        Item target = ItemOf(92);
        target.Socket = new ItemSocket(2, 0);
        target.Transfer = null;

        ByteWriter writer = ItemSocketPacket.TransferSocket(source, target);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemSocketSystem));
        Assert.That(reader.ReadByte(), Is.EqualTo(13));
        Assert.That(reader.ReadLong(), Is.EqualTo(91));
        Assert.That(reader.ReadByte(), Is.EqualTo(3)); // max slots
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // unlocked slots
        Assert.That(reader.ReadBool(), Is.False); // slot 0 holds no gem
        Assert.That(reader.ReadLong(), Is.EqualTo(92));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadBool(), Is.False); // no transfer block
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 3 + 8 + 2 + 1));
    }

    [Test]
    public void TransferSocketCarriesTargetTransferBlock() {
        Item source = ItemOf(91);
        Item target = ItemOf(92);
        target.Transfer = new ItemTransfer(TransferFlag.Trade, 5, 1);
        var block = new ByteWriter();
        block.WriteClass<ItemTransfer>(target.Transfer);

        ByteWriter writer = ItemSocketPacket.TransferSocket(source, target);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemSocketSystem));
        Assert.That(reader.ReadByte(), Is.EqualTo(13));
        Assert.That(reader.ReadLong(), Is.EqualTo(91));
        Assert.That(reader.ReadShort(), Is.EqualTo(0)); // default socket block
        Assert.That(reader.ReadLong(), Is.EqualTo(92));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Read<TransferFlag>(), Is.EqualTo(TransferFlag.Trade));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadInt(), Is.EqualTo(5)); // remaining trades
        Assert.That(reader.ReadInt(), Is.EqualTo(1)); // repackage count
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 2 + 8 + 2 + 1 + block.Length));
    }
}

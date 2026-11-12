using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

public class FieldDropItemTests {
    private const int ItemId = 39990001;

    private static Item ItemOf(TransferType transferType) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, (int) transferType, [], []);
        var metadata = new ItemMetadata(ItemId, ItemId.ToString(), [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 1, 3);
    }

    // CSceneStateGame::OnFieldAddItem: objId, itemId, amount, long receiver, Vector3, owner objId, u8 type, int rarity,
    // u16 drop kind, bool fixed, bool, then the item stream.
    [Test]
    public void DropItemWritesDropKindAndReceiver() {
        var fieldItem = new FieldItem(null!, 7001, ItemOf(TransferType.Tradable)) {
            Position = new Vector3(10, 20, 30),
            ReceiverId = 4000000001,
            Type = DropType.Default,
            DropKind = ItemDropKind.NpcDeath,
        };

        var reader = new ByteReader(FieldPacket.DropItem(fieldItem).Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FieldAddItem));
        Assert.That(reader.ReadInt(), Is.EqualTo(7001));
        Assert.That(reader.ReadInt(), Is.EqualTo(ItemId));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(4000000001));
        reader.Skip(12);
        Assert.That(reader.ReadInt(), Is.EqualTo(0)); // no owner object
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) DropType.Default));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo(21));
    }

    // KMS2 sends an item without a colour as colour index -1 (item stream offset 68).
    [Test]
    public void ItemWithoutColourSendsIndexMinusOne() {
        Item item = ItemOf(TransferType.Tradable);
        Assert.That(item.Appearance!.Color.Index, Is.EqualTo(-1));
        Assert.That(ItemAppearance.Default.Color.Index, Is.EqualTo(-1));
        Assert.That(EquipColor.None.Index, Is.EqualTo(-1));

        var writer = new ByteWriter();
        writer.WriteClass<Item>(item);
        var reader = new ByteReader(writer.Buffer, 0);
        reader.Skip(68);
        Assert.That(reader.ReadInt(), Is.EqualTo(-1));
    }

    // The client only knows Trade (4), Bind (8) and LimitTrade (16); Split stays server-side and is not sent.
    [Test]
    public void TransferFlagOmitsSplitOnTheWire() {
        var transfer = new ItemTransfer(TransferFlag.Trade | TransferFlag.Split, 2);
        var writer = new ByteWriter();
        writer.WriteClass<ItemTransfer>(transfer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(transfer.Flag.HasFlag(TransferFlag.Split), Is.True);

        var bound = new ItemTransfer(TransferFlag.Bind | TransferFlag.Trade | TransferFlag.Split);
        writer = new ByteWriter();
        writer.WriteClass<ItemTransfer>(bound);
        Assert.That(new ByteReader(writer.Buffer, 0).ReadInt(), Is.EqualTo(12));
    }

    [Test]
    public void DropKindsPerSource() {
        Assert.That(ItemDropKind.NpcDeath, Is.EqualTo(21));
        Assert.That(ItemDropKind.NpcHit, Is.EqualTo(24));
        Assert.That(ItemDropKind.InteractObject, Is.EqualTo(33));
        Assert.That(ItemDropKind.PlayerDrop, Is.EqualTo(1005));
        Assert.That(ItemDropKind.Trigger, Is.EqualTo(1062));
        Assert.That(ItemDropKind.Mastery + (int) MasteryType.Mining, Is.EqualTo(3103));
        Assert.That(ItemDropKind.Mastery + (int) MasteryType.Farming, Is.EqualTo(3106));
        Assert.That(ItemDropKind.Pet, Is.EqualTo(3200));
    }

    [Test]
    public void DropDurations() {
        Assert.That(FieldItem.DefaultDurationMs, Is.EqualTo(90_000));
        Assert.That(FieldItem.PlayerDropDurationMs, Is.EqualTo(60_000));
        Assert.That(FieldItem.SpawnPointDurationMs(0), Is.EqualTo(0)); // default
        Assert.That(FieldItem.SpawnPointDurationMs(8), Is.EqualTo(8_000));
        Assert.That(FieldItem.SpawnPointDurationMs(1e9f), Is.EqualTo(int.MaxValue));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketSystem 12, socket transfer (ItemSocketSystemPageSocketTransfer::RequestTransfer and the rules in
// itemsocketsystemutil.cpp: FUN_140627e80, FUN_140628050, FUN_140628200, GetSocketTransferCost).
public class SocketTransferHandlerTests {
    private static readonly ItemSocketTable Table = new(new Dictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>> {
        [9001] = new Dictionary<int, ItemSocketMetadata> { [0] = new(2, 0) },
        [9005] = new Dictionary<int, ItemSocketMetadata> { [0] = new(4, 0) },
        [1002] = new Dictionary<int, ItemSocketMetadata> { [0] = new(2, 2) },
    });

    private TestGameSession test = null!;
    private Item source = null!;
    private Item target = null!;
    private Item crystals = null!;

    // An outfit top (subtype 14) with a skin type and the socket id.
    private static ItemMetadata Outfit(int id, int socketId = 9001, int skinType = 1, int subType = 14) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 1);
        return metadata with {
            Property = metadata.Property with { IsSkin = true, SkinType = skinType, SubType = subType, SocketId = socketId, SlotMax = 1 },
        };
    }

    private GameSession Start(ItemMetadata sourceData, ItemMetadata targetData, int crystalCount = 1000) {
        test = new TestGameSession();
        test.SetTable(Table);
        source = test.Store(sourceData);
        target = test.Store(targetData);
        ItemMetadata crystal = TestGameSession.Metadata(30000001, type: 2);
        if (crystalCount > 0) {
            crystals = test.Store(crystal with { Property = crystal.Property with { Tag = ItemTag.SkinCrystal } }, amount: crystalCount);
        }
        GameSession session = test.Start();
        test.TakeSent();
        source = session.Item.Inventory.Get(source.Uid)!;
        target = session.Item.Inventory.Get(target.Uid)!;
        source.Socket = new ItemSocket(2, 2);
        target.Socket = new ItemSocket(2, 0);
        return session;
    }

    private void Transfer(GameSession session, long sourceUid, long targetUid) {
        new ItemSocketHandler { ItemMetadata = null!, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteLong(sourceUid);
            writer.WriteLong(targetUid);
        }));
    }

    // The open sockets move, 200 crystals each are paid, and the target comes back unable to pass them on.
    [Test]
    public void Transfer_MovesTheOpenSockets() {
        GameSession session = Start(Outfit(11400001), Outfit(11400002));

        Transfer(session, source.Uid, target.Uid);

        Assert.That(source.Socket!.UnlockSlots, Is.EqualTo(0));
        Assert.That(target.Socket!.UnlockSlots, Is.EqualTo(2));
        Assert.That(target.Transfer!.SocketTransfer, Is.False);
        Assert.That(source.Transfer!.SocketTransfer, Is.True);
        Assert.That(session.Item.Inventory.Get(crystals.Uid)!.Amount, Is.EqualTo(600));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.TransferSocket(source, target).ToArray()) }));

        // The target may not give them on.
        Assert.That(ItemSocketHandler.CanGiveSockets(target, Table), Is.False);
    }

    // With ItemSocketTransfer01 (id 167) off the dialog has no transfer tab (uiitemsocketsystemdialog.cpp:171/243),
    // so a transfer request is dropped: nothing moves, nothing is paid and nothing is sent.
    [Test]
    public void TransferFeatureOff_IsDropped() {
        GameSession session = Start(Outfit(11400001), Outfit(11400002));
        test.SetFeaturesOff(ItemSocketRules.TransferFeature);

        Transfer(session, source.Uid, target.Uid);

        Assert.Multiple(() => {
            Assert.That(source.Socket!.UnlockSlots, Is.EqualTo(2));
            Assert.That(target.Socket!.UnlockSlots, Is.EqualTo(0));
            Assert.That(session.Item.Inventory.Get(crystals.Uid)!.Amount, Is.EqualTo(1000));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void ShortOfCrystals_LackPrice() {
        GameSession session = Start(Outfit(11400001), Outfit(11400002), crystalCount: 399);

        Transfer(session, source.Uid, target.Uid);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price).ToArray()) }));
        Assert.That(source.Socket!.UnlockSlots, Is.EqualTo(2));
        Assert.That(target.Socket!.UnlockSlots, Is.EqualTo(0));
        Assert.That(session.Item.Inventory.Get(crystals.Uid)!.Amount, Is.EqualTo(399));
    }

    // A target with a different socket count, a gem in the source, a target with an open socket or a missing item:
    // invalid target, nothing moves and nothing is paid.
    [TestCase("maxSlots")]
    [TestCase("gem")]
    [TestCase("targetOpen")]
    [TestCase("missing")]
    [TestCase("same")]
    public void BadPair_InvalidTarget(string why) {
        GameSession session = Start(Outfit(11400001), Outfit(11400002));
        long targetUid = target.Uid;
        switch (why) {
            case "maxSlots":
                target.Socket = new ItemSocket(4, 0);
                break;
            case "gem":
                source.Socket!.Sockets[0] = new ItemGemstone(40000001, stats: new ItemStats());
                break;
            case "targetOpen":
                target.Socket = new ItemSocket(2, 1);
                break;
            case "missing":
                targetUid = 123456789;
                break;
            case "same":
                targetUid = source.Uid;
                break;
        }

        Transfer(session, source.Uid, targetUid);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target).ToArray()) }));
        Assert.That(session.Item.Inventory.Get(crystals.Uid)!.Amount, Is.EqualTo(1000));
    }

    // Two Northern Fur Earrings 11200084 (type 1, skin type 2, socketDataId 9001, two sockets) and 450 of each item
    // the client tags SkinCrystal (40100027, 40100032, 40100053; type 15). The server pack gives the earring subtype 0
    // where the client's item data gives 12, and the server asked its own subtype, so it refused what the client
    // offered with invalid_target ("The selected target is not in your inventory."). The rule now asks the client's
    // subtype, the id's digits (GearScoreRules.SubType).
    private GameSession StartEarrings(int id = 11200084) {
        test = new TestGameSession();
        test.SetTable(Table);
        source = test.Store(Outfit(id, skinType: 2, subType: 0));
        target = test.Store(Outfit(id, skinType: 2, subType: 0));
        foreach (int crystalId in new[] { 40100027, 40100032, 40100053 }) {
            ItemMetadata crystal = TestGameSession.Metadata(crystalId, type: 15, slotMax: 99999);
            test.Store(crystal with { Property = crystal.Property with { Tag = ItemTag.SkinCrystal } }, amount: 450);
        }
        GameSession session = test.Start();
        test.TakeSent();
        source = session.Item.Inventory.Get(source.Uid)!;
        target = session.Item.Inventory.Get(target.Uid)!;
        source.Socket = new ItemSocket(2, 2);
        target.Socket = new ItemSocket(2, 0);
        return session;
    }

    private static int Crystals(GameSession session) =>
        session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ItemTag.SkinCrystal).Sum(item => item.Amount);

    [Test]
    public void NorthernFurEarrings_MoveForFourHundredCrystals() {
        GameSession session = StartEarrings();
        Assert.That(source.Inventory, Is.EqualTo(InventoryType.Outfit));
        Assert.That(target.Inventory, Is.EqualTo(InventoryType.Outfit));

        Transfer(session, source.Uid, target.Uid);

        Assert.That((source.Socket!.UnlockSlots, target.Socket!.UnlockSlots), Is.EqualTo(((byte) 0, (byte) 2)));
        Assert.That(Crystals(session), Is.EqualTo(3 * 450 - 400));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.TransferSocket(source, target).ToArray()) }));
    }

    // An outfit whose id digits are no socket subtype (119xxxxx, a necklace: 19) is still refused, whatever the
    // server pack's subtype; nothing moves and nothing is paid.
    [Test]
    public void OutfitNecklace_InvalidTarget() {
        GameSession session = StartEarrings(11900084);

        Transfer(session, source.Uid, target.Uid);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target).ToArray()) }));
        Assert.That((source.Socket!.UnlockSlots, target.Socket!.UnlockSlots), Is.EqualTo(((byte) 2, (byte) 0)));
        Assert.That(Crystals(session), Is.EqualTo(3 * 450));
    }

    // FUN_140627e80 / FUN_140628050 and what they ask of the item (HasSkinType, CanTransferSocket, FUN_140529560).
    [Test]
    public void Rules() {
        var item = new Item(Outfit(11400001)) { Socket = new ItemSocket(2, 2) };
        Assert.That(ItemSocketHandler.CanGiveSockets(item, Table), Is.True);
        Assert.That(ItemSocketHandler.CanTakeSockets(item, Table), Is.False);

        item.GachaDismantleId = 5;
        Assert.That(ItemSocketHandler.CanGiveSockets(item, Table), Is.False);
        item.GachaDismantleId = 0;

        item.Transfer!.SocketTransfer = false;
        Assert.That(ItemSocketHandler.CanGiveSockets(item, Table), Is.False);
        item.Transfer.SocketTransfer = true;

        // Opened by its row (max 2, initial 2): CItemSocketTable::FUN_14062d980 says no.
        Assert.That(ItemSocketHandler.CanGiveSockets(new Item(Outfit(11400001, socketId: 1002)) { Socket = new ItemSocket(2, 2) }, Table), Is.False);
        // A socket id without a row says no; no socket id says yes.
        Assert.That(ItemSocketHandler.CanGiveSockets(new Item(Outfit(11400001, socketId: 7777)) { Socket = new ItemSocket(2, 2) }, Table), Is.False);
        Assert.That(ItemSocketHandler.CanGiveSockets(new Item(Outfit(11400001, socketId: 0)) { Socket = new ItemSocket(2, 2) }, Table), Is.True);
        // A timed item without a socket id says no.
        Assert.That(ItemSocketHandler.CanGiveSockets(new Item(Outfit(11400001, socketId: 0)) { Socket = new ItemSocket(2, 2), ExpiryTime = 100 }, Table), Is.False);
        // No skin type, or an outfit subtype the socket system does not take (19, a necklace).
        Assert.That(ItemSocketHandler.CanGiveSockets(new Item(Outfit(11400001, skinType: 0)) { Socket = new ItemSocket(2, 2) }, Table), Is.False);
        Assert.That(ItemSocketHandler.CanGiveSockets(new Item(Outfit(11900001, subType: 19)) { Socket = new ItemSocket(2, 2) }, Table), Is.False);

        var empty = new Item(Outfit(11400002, socketId: 9005)) { Socket = new ItemSocket(4, 0) };
        Assert.That(ItemSocketHandler.CanTakeSockets(empty, Table), Is.True);
        Assert.That(ItemSocketHandler.CanTransferSockets(item, empty, Table), Is.False);
    }

    [TestCase(14, 1, true)]
    [TestCase(22, 1, true)]
    [TestCase(19, 1, false)]
    [TestCase(21, 1, false)]
    [TestCase(19, 0, true)]
    [TestCase(14, 0, false)]
    public void HasSocketSubtype(int subType, int skinType, bool expected) {
        Assert.That(ItemSocketRules.HasSocketSubtype(subType, skinType), Is.EqualTo(expected));
    }

    [TestCase(2, 1, 400)]
    [TestCase(4, 99, 800)]
    [TestCase(2, 0, 0)]
    public void TransferCost(int open, int skinType, int expected) {
        Assert.That(ItemSocketRules.TransferCost(open, skinType), Is.EqualTo(expected));
        Assert.That(ItemTagMap.ToClient(ItemSocketRules.TransferCostTag), Is.EqualTo(0x58));
    }

    // The flag goes out as CItemTransfer m_b42 and is read back.
    [TestCase(true)]
    [TestCase(false)]
    public void SocketTransferFlag_OnTheWire(bool flag) {
        var transfer = new ItemTransfer(TransferFlag.Trade) { SocketTransfer = flag };
        var writer = new Maple2.PacketLib.Tools.ByteWriter();
        writer.WriteClass<ItemTransfer>(transfer);
        byte[] bytes = writer.ToArray();
        Assert.That(bytes[14], Is.EqualTo(flag ? 1 : 0));
        var read = new Maple2.PacketLib.Tools.ByteReader(bytes).ReadClass<ItemTransfer>();
        Assert.That(read.SocketTransfer, Is.EqualTo(flag));
        Assert.That(transfer.Clone().SocketTransfer, Is.EqualTo(flag));
    }

    // The flag is kept in the item row, and a row without it reads as true.
    [Test]
    public void SocketTransferFlag_IsSaved() {
        test = new TestGameSession();
        Item item = test.Store(Outfit(11400001));
        Assert.That(item.Transfer!.SocketTransfer, Is.True);
        item.Transfer.SocketTransfer = false;
        using (var db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, item), Is.True);
        }
        using (var db = test.Storage.Context()) {
            Assert.That(db.GetItem(item.Uid)!.Transfer!.SocketTransfer, Is.False);
        }
    }
}

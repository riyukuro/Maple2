using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game;

// The CItemTransfer flags a new item is given: m_b42 (socket transfer) from CItemTransfer::InitFromItem, m_b3F (no
// dismantle) from a drop row's disableBreak and on an extraction result; and the socket row's openType.
public class ItemCreationTransferTests {
    private const int BoxId = 900001;

    private static readonly ItemSocketTable Table = new(new Dictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>> {
        [9001] = new Dictionary<int, ItemSocketMetadata> { [0] = new(2, 0) },
        [9005] = new Dictionary<int, ItemSocketMetadata> { [0] = new(4, 0), [4] = new(4, 4) },
        [1002] = new Dictionary<int, ItemSocketMetadata> { [0] = new(2, 2) },
        [1006] = new Dictionary<int, ItemSocketMetadata> { [0] = new(4, 4) },
        // openType 1: the client opens none at creation and reads no fixOpenCount (GetInitialSocketCount).
        [1100] = new Dictionary<int, ItemSocketMetadata> { [0] = new(4, 4, OpenType: 1) },
    });

    private static ItemMetadata Outfit(int id, int socketId = 9001) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 1);
        return metadata with {
            Property = metadata.Property with { IsSkin = true, SkinType = 1, SubType = 14, SocketId = socketId, SlotMax = 1 },
        };
    }

    // InitFromItem: m_b42 = m_b41 (CItemSocketTable::FUN_14062d980) of the item's own row: 9001-9008 give 1. For the
    // socketDataID of a drop row (1002 or 1006, 2/2 and 4/4 open) the same rule is chosen here, which gives 0.
    [TestCase(9001, 0, 1, true)]
    [TestCase(9001, 1002, 1, false)]
    [TestCase(9001, 1006, 1, false)]
    [TestCase(0, 0, 1, true)]       // no socketDataId
    [TestCase(7777, 0, 1, false)]   // a socketDataId without a row
    [TestCase(9005, 0, 1, true)]    // the grade 0 row (4, 0)
    [TestCase(9005, 0, 4, false)]   // the grade 4 row (4, 4)
    [TestCase(1100, 0, 1, true)]    // openType 1: 4 > 0, whatever fixOpenCount says
    public void SocketTransferAtCreation(int ownSocketId, int dropSocketId, int rarity, bool expected) {
        Assert.That(ItemStatsCalculator.SocketTransferAtCreation(Outfit(11400001, ownSocketId), rarity, Table, dropSocketId),
            Is.EqualTo(expected));
    }

    // FUN_14052b370 opens GetInitialSocketCount sockets: fixOpenCount for openType 0, none for openType 1.
    [Test]
    public void OpenType1_OpensNoSocketAtCreation() {
        Assert.That(new ItemSocketMetadata(4, 4, OpenType: 1).InitialCount, Is.EqualTo(0));
        Assert.That(new ItemSocketMetadata(4, 3).InitialCount, Is.EqualTo(3));
        ItemMetadata outfit = Outfit(11400001, 1100);
        ItemSocket? sockets = ItemStatsCalculator.CreateSockets(outfit, new ItemType(outfit.Id), 1, Table);
        Assert.That((sockets!.MaxSlots, sockets.UnlockSlots), Is.EqualTo(((byte) 4, (byte) 0)));
    }

    // A drop row with socketDataID 1002 and disableBreak: m_b42 0 (the drop row rule chosen above) and m_b3F 1.
    [TestCase(1002, true, false, true)]
    [TestCase(0, false, true, false)]
    public void DropRow_SetsTheTransferFlags(int socketDataId, bool disableBreak, bool socketTransfer, bool noBreak) {
        var test = new TestGameSession();
        ItemMetadata outfit = Outfit(11400001);
        test.Register(outfit);
        test.SetTable(Table);
        IDictionary<int, IndividualDropItemTable.Entry> box = TestGameSession.DropBox([(outfit.Id, 1)]);
        IndividualDropItemTable.Entry group = box[1];
        box[1] = group with { Items = [group.Items[0] with { SocketDataId = socketDataId, DisableBreak = disableBreak }] };
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> { [BoxId] = box }));
        GameSession session = test.Start();

        Item item = session.Field!.ItemDrop.GetIndividualDropItems(BoxId).Single();

        Assert.That(item.Socket!.UnlockSlots, Is.EqualTo(socketDataId == 1002 ? 2 : 0));
        Assert.That(item.Transfer!.SocketTransfer, Is.EqualTo(socketTransfer));
        Assert.That(item.Transfer.DisableBreak, Is.EqualTo(noBreak));
    }

    // CreateItem (any item made by the server) applies the item's own row.
    [TestCase(9001, true)]
    [TestCase(1002, false)]
    public void CreateItem_SetsSocketTransfer(int socketId, bool expected) {
        var test = new TestGameSession();
        ItemMetadata outfit = Outfit(11400001, socketId);
        test.Register(outfit);
        test.SetTable(Table);
        GameSession session = test.Start();

        Item item = session.Field!.ItemDrop.CreateItem(outfit.Id)!;

        Assert.That(item.Transfer!.SocketTransfer, Is.EqualTo(expected));
        Assert.That(item.Transfer.DisableBreak, Is.False);
    }

    // With ItemSocketSystem (id 46) off the client counts no sockets: a created item and a drop row's socketDataID get none.
    [Test]
    public void SocketSystemOff_CreatesNoSockets() {
        var test = new TestGameSession();
        ItemMetadata outfit = Outfit(11400001);
        test.Register(outfit);
        test.SetTable(Table);
        IDictionary<int, IndividualDropItemTable.Entry> box = TestGameSession.DropBox([(outfit.Id, 1)]);
        IndividualDropItemTable.Entry group = box[1];
        box[1] = group with { Items = [group.Items[0] with { SocketDataId = 1002 }] };
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> { [BoxId] = box }));
        GameSession session = test.Start();
        test.SetFeaturesOff(ItemSocketRules.SystemFeature);

        Assert.Multiple(() => {
            Assert.That(session.Field!.ItemDrop.CreateItem(outfit.Id)!.Socket, Is.Null);
            Assert.That(session.Field!.ItemDrop.GetIndividualDropItems(BoxId).Single().Socket, Is.Null);
        });
    }

    // m_b3F is the byte before m_b42 (CItemTransfer::vfunc_2); it is read back, cloned and saved.
    [TestCase(true)]
    [TestCase(false)]
    public void DisableBreak_OnTheWire(bool flag) {
        var transfer = new ItemTransfer(TransferFlag.Trade) { DisableBreak = flag };
        var writer = new ByteWriter();
        writer.WriteClass<ItemTransfer>(transfer);
        byte[] bytes = writer.ToArray();
        Assert.That(bytes[13], Is.EqualTo(flag ? 1 : 0));
        Assert.That(bytes[14], Is.EqualTo(1));
        Assert.That(new ByteReader(bytes).ReadClass<ItemTransfer>().DisableBreak, Is.EqualTo(flag));
        Assert.That(transfer.Clone().DisableBreak, Is.EqualTo(flag));
        Assert.That(transfer.Clone(), Is.EqualTo(transfer));
    }

    [Test]
    public void DisableBreak_IsSaved() {
        var test = new TestGameSession();
        Item item = test.Store(Outfit(11400001));
        Assert.That(item.Transfer!.DisableBreak, Is.False);
        item.Transfer.DisableBreak = true;
        using (var db = test.Storage.Context()) {
            Assert.That(db.SaveItems(test.CharacterId, item), Is.True);
        }
        using (var db = test.Storage.Context()) {
            Assert.That(db.GetItem(item.Uid)!.Transfer!.DisableBreak, Is.True);
        }
    }

    // The extraction result, as the client previews it: no socket transfer, no dismantle.
    [Test]
    public void ExtractionResult_IsMarked() {
        var item = new Item(Outfit(11400001));
        ItemExtractionHandler.MarkExtracted(item);
        Assert.That(item.Transfer!.SocketTransfer, Is.False);
        Assert.That(item.Transfer.DisableBreak, Is.True);
    }

    // CItemBreakManager::CanBreakItem: a gacha dismantle item passes first; others need enableBreak and no m_b3F.
    [TestCase(true, false, 0, true)]
    [TestCase(true, true, 0, false)]
    [TestCase(false, false, 0, false)]
    [TestCase(false, true, 5, true)]
    public void Breakable(bool enableBreak, bool disableBreak, int gachaDismantleId, bool expected) {
        Assert.That(DismantleRules.Breakable(enableBreak, disableBreak, gachaDismantleId), Is.EqualTo(expected));
    }

    // The client asks feature 0x2e (ItemSocketSystem) in FUN_140529560 and builds the transfer page only with 0xa7
    // (ItemSocketTransfer01).
    [TestCase(true, true, true)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    public void SocketTransfer_FeatureGates(bool socketSystem, bool socketTransfer, bool expected) {
        ItemSocketTable table = Table with { SocketSystem = socketSystem, SocketTransfer = socketTransfer };
        var source = new Item(Outfit(11400001)) { Socket = new ItemSocket(2, 2) };
        var target = new Item(Outfit(11400002)) { Socket = new ItemSocket(2, 0) };
        Assert.That(ItemSocketHandler.CanTransferSockets(source, target, table), Is.EqualTo(expected));
        Assert.That(ItemSocketHandler.CanGiveSockets(source, table), Is.EqualTo(socketSystem));
    }
}

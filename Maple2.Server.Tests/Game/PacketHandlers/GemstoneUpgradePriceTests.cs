using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketSystem 4, the price of a gem upgrade (FUN_140f76970): each ingredient's count lowered by the
// ReduceGemstoneUpgradeFee attribute, crystal fragments short paid at 1000 mesos each, any other ingredient short
// refused with s_itemsocketsystem_error_lack_price, and a bare-id ingredient (another copy of the gem) counted by its
// id with the gem being upgraded left out of its own stack (CItem::vfunc_4).
public class GemstoneUpgradePriceTests {
    private static readonly int[] SureRates = [0, 0, 100, 100, 100, 100, 100, 100, 100, 100, 100];

    private TestGameSession test = null!;
    private Item gear = null!;
    private Item dust = null!;
    private Item bagGem = null!;
    private Item? chips;

    private GameSession Start(GemstoneUpgradeTable.Ingredient[] ingredients, int chipCount = 0, long mesos = 0, int bagGems = 3) {
        test = new TestGameSession();
        test.SetTable(new GemstoneUpgradeTable(new Dictionary<int, GemstoneUpgradeTable.Entry> {
            [40200001] = new(1, 40200002, ingredients, 3),
        }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { GemStoneProbList = SureRates, SkinGemStoneProbList = SureRates });
        gear = test.Store(TestGameSession.Metadata(11200001, type: 1));
        ItemMetadata dustData = TestGameSession.Metadata(40300001, type: 2, slotMax: 1000);
        dust = test.Store(dustData with { Property = dustData.Property with { Tag = ItemTag.DustBlue } }, amount: 100);
        if (chipCount > 0) {
            ItemMetadata chipData = TestGameSession.Metadata(40100001, type: 2, slotMax: 1000);
            chips = test.Store(chipData with { Property = chipData.Property with { Tag = ItemTag.CrystalPiece } }, amount: chipCount);
        }
        bagGem = test.Store(TestGameSession.Metadata(40200001, type: 13, slotMax: 9999), amount: bagGems);
        test.Register(TestGameSession.Metadata(40200002, type: 13, slotMax: 9999));
        GameSession session = test.Start();
        test.TakeSent();
        session.Currency.Meso = mesos;
        gear = session.Item.Inventory.Get(gear.Uid)!;
        bagGem = session.Item.Inventory.Get(bagGem.Uid, InventoryType.Gemstone)!;
        gear.Socket = new ItemSocket(3, 1);
        gear.Socket.Sockets[0] = new ItemGemstone(40200001, stats: new ItemStats());
        return session;
    }

    private void UpgradeSocketed(GameSession session) {
        Handle(session, gear.Uid, 0, 77);
    }

    private void UpgradeInBag(GameSession session) {
        Handle(session, 0, 0xFF, bagGem.Uid);
    }

    private void Handle(GameSession session, long itemUid, byte slot, long gemUid) {
        new ItemSocketHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteLong(itemUid);
            writer.WriteByte(slot);
            writer.WriteLong(gemUid);
        }));
    }

    private int Amount(GameSession session, Item? item, InventoryType? type = null) {
        if (item == null) {
            return 0;
        }
        return (type == null ? session.Item.Inventory.Get(item.Uid) : session.Item.Inventory.Get(item.Uid, type.Value))?.Amount ?? 0;
    }

    private string[] Sent() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString).ToArray();
    }

    private static string Error(Maple2.Model.Error.ItemSocketError error) => Convert.ToHexString(ItemSocketPacket.Error(error: error).ToArray());

    [Test]
    public void FragmentsShort_ArePaidInMesos() {
        GameSession session = Start([new GemstoneUpgradeTable.Ingredient(ItemTag.CrystalPiece, 400)], chipCount: 150, mesos: 300000);

        UpgradeSocketed(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200002));
            Assert.That(Amount(session, chips), Is.Zero);
            Assert.That(session.Currency.Meso, Is.EqualTo(50000));
        });
    }

    [Test]
    public void MesosShort_TakeNothing() {
        GameSession session = Start([new GemstoneUpgradeTable.Ingredient(ItemTag.DustBlue, 10), new GemstoneUpgradeTable.Ingredient(ItemTag.CrystalPiece, 400)],
            chipCount: 150, mesos: 249999);

        UpgradeSocketed(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200001));
            Assert.That(Amount(session, chips), Is.EqualTo(150));
            Assert.That(Amount(session, dust), Is.EqualTo(100));
            Assert.That(session.Currency.Meso, Is.EqualTo(249999));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_lack_price) }));
        });
    }

    // KR 40220131-40220202: the level 1 and 2 rows take copies of the level 1 gem by its bare id.
    [Test]
    public void CopyOfTheGem_IsTakenFromItsOwnStack() {
        GameSession session = Start([new GemstoneUpgradeTable.Ingredient(ItemTag.None, 1, 40200001)], bagGems: 3);

        UpgradeInBag(session);

        Assert.Multiple(() => {
            Assert.That(Amount(session, bagGem, InventoryType.Gemstone), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Find(40200002).Sum(item => item.Amount), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
            Assert.That(Amount(session, dust), Is.EqualTo(100));
        });
    }

    // The gem being upgraded is not its own copy: a stack of one has none to give.
    [Test]
    public void TheGemItself_IsNotItsCopy() {
        GameSession session = Start([new GemstoneUpgradeTable.Ingredient(ItemTag.None, 1, 40200001)], bagGems: 1);

        UpgradeInBag(session);

        Assert.Multiple(() => {
            Assert.That(Amount(session, bagGem, InventoryType.Gemstone), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Find(40200002), Is.Empty);
            Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
            Assert.That(Amount(session, dust), Is.EqualTo(100));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_lack_price) }));
        });
    }

    // A socketed gem's copies come from the bag, the whole stack.
    [Test]
    public void SocketedGem_TakesCopiesFromTheBag() {
        GameSession session = Start([new GemstoneUpgradeTable.Ingredient(ItemTag.None, 3, 40200001)], bagGems: 3);

        UpgradeSocketed(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200002));
            Assert.That(Amount(session, bagGem, InventoryType.Gemstone), Is.Zero);
            Assert.That(Amount(session, dust), Is.EqualTo(100));
        });
    }

    // ReduceGemstoneUpgradeFee 100 (10%): 10 dust become 9.
    [Test]
    public void FeeAttribute_LowersTheCounts() {
        GameSession session = Start([new GemstoneUpgradeTable.Ingredient(ItemTag.DustBlue, 10)]);
        session.Player.Stats.Values[SpecialAttribute.ReduceGemstoneUpgradeFee].AddTotal(100);

        UpgradeSocketed(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200002));
            Assert.That(Amount(session, dust), Is.EqualTo(91));
        });
    }
}

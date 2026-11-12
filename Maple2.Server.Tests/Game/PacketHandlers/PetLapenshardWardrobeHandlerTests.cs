using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The pet bag's load, the lapenshard upgrade and the closet's equip and reset, as KMS2 answers them.
public class PetLapenshardWardrobeHandlerTests {
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // The bag request of a summoned pet: the reset alone for an empty bag, the reset then the list otherwise.
    [Test]
    public void PetBag_LoadSendsTheResetThenTheItems() {
        Assert.That(PetManager.LoadInventoryPackets([]).Select(Hex), Is.EqualTo(new[] { Hex(PetInventoryPacket.Reset()) }));

        var potion = new Item(TestGameSession.Metadata(20000001, type: 2), amount: 3) { Uid = 77 };
        Assert.That(PetManager.LoadInventoryPackets([potion]).Select(Hex), Is.EqualTo(new[] {
            Hex(PetInventoryPacket.Reset()),
            Hex(PetInventoryPacket.Load([potion])),
        }));
    }

    private const int ShardId = 41000001;
    private const int NextShardId = 41000002;

    // A bag lapenshard upgraded with pieces of its own stack: the fodder leaves first, then the meso, the
    // ingredients and the lapenshard itself; the new item enters the bag and the answer carries uid 0.
    [Test]
    public void LapenshardUpgrade_TakesFodderMesoIngredientsThenTheShard() {
        var test = new TestGameSession();
        ItemMetadata crystalData = TestGameSession.Metadata(40100001, type: 2);
        crystalData = crystalData with { Property = crystalData.Property with { Tag = ItemTag.RedCrystal } };
        Item shards = test.Store(TestGameSession.Metadata(ShardId, type: 21), amount: 9);
        Item crystals = test.Store(crystalData, amount: 50);
        test.Register(TestGameSession.Metadata(NextShardId, type: 21));
        test.SetTable(new LapenshardUpgradeTable(new Dictionary<int, LapenshardUpgradeTable.Entry> {
            [ShardId] = new(1, 1, NextShardId, 4, [new LapenshardUpgradeTable.Ingredient(ItemTag.RedCrystal, 20)], 1000),
        }));
        GameSession session = test.Start();
        var handler = new LapenshardHandler { TableMetadata = test.TableMetadata, ItemMetadata = session.ItemMetadata };
        session.Currency.Meso = 5000;
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(shards.Uid);
            writer.WriteInt(ShardId);
            writer.WriteInt(0);
            writer.WriteInt(1);
            writer.WriteLong(shards.Uid);
            writer.WriteInt(4);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Take(4).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemInventoryPacket.UpdateAmount(shards.Uid, 5)),
            Convert.ToHexString(sent[1]),
            Hex(ItemInventoryPacket.UpdateAmount(crystals.Uid, 30)),
            Hex(ItemInventoryPacket.UpdateAmount(shards.Uid, 4)),
        }));
        Assert.That(TestGameSession.Op(sent[1]), Is.EqualTo(SendOp.Meso));
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(LapenshardPacket.Upgrade(0, ShardId, (LapenshardSlot) 0))));
        Assert.That(session.Currency.Meso, Is.EqualTo(4000));
        Assert.That(session.Item.Inventory.Find(NextShardId).Sum(item => item.Amount), Is.EqualTo(1));
    }

    // Too few ingredients: nothing is taken.
    [Test]
    public void LapenshardUpgrade_WithoutIngredientsTakesNothing() {
        var test = new TestGameSession();
        Item shards = test.Store(TestGameSession.Metadata(ShardId, type: 21), amount: 9);
        test.Register(TestGameSession.Metadata(NextShardId, type: 21));
        test.SetTable(new LapenshardUpgradeTable(new Dictionary<int, LapenshardUpgradeTable.Entry> {
            [ShardId] = new(1, 1, NextShardId, 4, [new LapenshardUpgradeTable.Ingredient(ItemTag.RedCrystal, 20)], 1000),
        }));
        GameSession session = test.Start();
        var handler = new LapenshardHandler { TableMetadata = test.TableMetadata, ItemMetadata = session.ItemMetadata };
        session.Currency.Meso = 5000;
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(shards.Uid);
            writer.WriteInt(ShardId);
            writer.WriteInt(0);
            writer.WriteInt(1);
            writer.WriteLong(shards.Uid);
            writer.WriteInt(4);
        }));

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(session.Item.Inventory.Get(shards.Uid)?.Amount, Is.EqualTo(9));
    }

    private static Wardrobe Closet(GameSession session, int type, params (Item Item, EquipSlot Slot)[] items) {
        var wardrobe = new Wardrobe(type, "tab");
        foreach ((Item item, EquipSlot slot) in items) {
            wardrobe.Equips[slot] = new Wardrobe.Equip(item.Uid, item.Id, slot, item.Rarity);
        }
        typeof(ConfigManager).GetField("wardrobes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Config, new List<Wardrobe> { wardrobe });
        return wardrobe;
    }

    private static void ClosetRequest(GameSession session, byte command, bool layer) {
        new WardrobeHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(0);
            if (layer) {
                writer.WriteInt(0);
            }
        }));
    }

    // Notice 4 with flags 5 (chat line and alert) and the closet's success string.
    private const string Success = "0405000101000000250D000000000000";

    // KMS2 sends ItemPutOn before the bag remove for a closet item.
    [Test]
    public void ClosetEquip_EndsWithTheSuccessNotice() {
        var test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Closet(session, 0, (cap, EquipSlot.CP));
        test.TakeSent();

        ClosetRequest(session, 1, layer: true);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemPutOn, SendOp.ItemInventory, SendOp.Notice }));
        Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(ItemInventoryPacket.Remove(cap.Uid))));
        Assert.That(Convert.ToHexString(sent[^1])[4..], Is.EqualTo(Success));
        Assert.That(session.Item.Equips.Get(cap.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Get(cap.Uid), Is.Null);
    }

    // A closet item that takes a worn item's place keeps the plain equip's order: its bag slot is freed first.
    [Test]
    public void ClosetEquip_OverAWornItemRemovesFirst() {
        var test = new TestGameSession();
        Item worn = test.Store(TestGameSession.Metadata(11300002, EquipSlot.CP, slotMax: 1), group: ItemGroup.Gear, slot: EquipSlot.CP);
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Closet(session, 0, (cap, EquipSlot.CP));
        test.TakeSent();

        ClosetRequest(session, 1, layer: true);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.Remove(cap.Uid))));
        Assert.That(TestGameSession.Op(sent[^2]), Is.EqualTo(SendOp.ItemPutOn));
        Assert.That(session.Item.Equips.Get(cap.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Get(worn.Uid), Is.Not.Null);
    }

    // Every item already worn: the notice alone, no "do not have" box.
    [Test]
    public void ClosetEquip_OfWornItemsSendsTheNoticeAlone() {
        var test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1), group: ItemGroup.Gear, slot: EquipSlot.CP);
        GameSession session = test.Start();
        Closet(session, 0, (cap, EquipSlot.CP));
        test.TakeSent();

        ClosetRequest(session, 1, layer: true);

        Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)[4..]), Is.EqualTo(new[] { Success }));
    }

    [Test]
    public void ClosetEquip_OfAnItemThatIsGoneSaysSo() {
        var test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Closet(session, 0, (cap, EquipSlot.CP));
        Assert.That(session.Item.Inventory.Remove(cap.Uid, out _), Is.True);
        test.TakeSent();

        ClosetRequest(session, 1, layer: true);

        Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)[4..]), Is.EqualTo(new[] { "0405000101000000260D000000000000" }));
    }

    // A cleared tab goes back to type 0.
    [Test]
    public void ClosetReset_SendsTypeZero() {
        var test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Wardrobe wardrobe = Closet(session, 1, (cap, EquipSlot.CP));
        test.TakeSent();

        ClosetRequest(session, 2, layer: false);

        Assert.That(wardrobe.Type, Is.EqualTo(0));
        Assert.That(wardrobe.Equips, Is.Empty);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(WardrobePacket.Load(0, wardrobe)) }));
    }

    // Two upgrade requests with what one upgrade needs: the second finds the lapenshard gone and takes nothing.
    [Test]
    public void LapenshardUpgrade_TwiceWithOneSetPaysOnce() {
        var test = new TestGameSession();
        ItemMetadata crystalData = TestGameSession.Metadata(40100001, type: 2);
        crystalData = crystalData with { Property = crystalData.Property with { Tag = ItemTag.RedCrystal } };
        Item shards = test.Store(TestGameSession.Metadata(ShardId, type: 21), amount: 5);
        Item crystals = test.Store(crystalData, amount: 50);
        test.Register(TestGameSession.Metadata(NextShardId, type: 21));
        test.SetTable(new LapenshardUpgradeTable(new Dictionary<int, LapenshardUpgradeTable.Entry> {
            [ShardId] = new(1, 1, NextShardId, 4, [new LapenshardUpgradeTable.Ingredient(ItemTag.RedCrystal, 20)], 1000),
        }));
        GameSession session = test.Start();
        var handler = new LapenshardHandler { TableMetadata = test.TableMetadata, ItemMetadata = session.ItemMetadata };
        session.Currency.Meso = 5000;
        void Upgrade() {
            handler.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(5);
                writer.WriteLong(shards.Uid);
                writer.WriteInt(ShardId);
                writer.WriteInt(0);
                writer.WriteInt(1);
                writer.WriteLong(shards.Uid);
                writer.WriteInt(4);
            }));
        }
        Upgrade();
        test.TakeSent();

        Upgrade();

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(4000));
        Assert.That(session.Item.Inventory.Get(crystals.Uid)!.Amount, Is.EqualTo(30));
        Assert.That(session.Item.Inventory.Find(NextShardId).Sum(item => item.Amount), Is.EqualTo(1));
    }
}

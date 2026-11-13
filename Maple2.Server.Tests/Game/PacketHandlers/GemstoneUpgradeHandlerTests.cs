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

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketSystem 4 and 6, the gem upgrade and its stage: the rate comes from the server constants GemStoneProbList
// (SkinGemStoneProbList for an outfit gem) by the level the upgrade gives, and a failed roll keeps the gem, takes the
// ingredients and counts gemstone_upgrade_try and gemstone_upgrade_fail (23200028 "Luster Lost").
public class GemstoneUpgradeHandlerTests {
    private static readonly int[] KrRates = [0, 0, 25, 22, 19, 16, 14, 12, 10, 9, 8];
    private static readonly int[] KrSkinRates = [0, 0, 80, 60, 40, 20, 20, 20, 20, 20, 20];

    private TestGameSession test = null!;
    private Item gear = null!;
    private Item dust = null!;

    private Item bagGem = null!;

    private GameSession Start(int[] rates, int equipPart = 3, int[]? skinRates = null, params GemstoneUpgradeTable.Ingredient[] more) {
        test = new TestGameSession();
        test.SetTable(new GemstoneUpgradeTable(new Dictionary<int, GemstoneUpgradeTable.Entry> {
            [40200001] = new(1, 40200002, [new GemstoneUpgradeTable.Ingredient(ItemTag.DustBlue, 10), .. more], equipPart),
        }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { GemStoneProbList = rates, SkinGemStoneProbList = skinRates ?? rates });
        test.AddAchievement(TestGameSession.Trophy(23200028, ConditionType.gemstone_upgrade_fail, [1, 10, 100]),
            TestGameSession.Trophy(23200029, ConditionType.gemstone_upgrade_success, [1]));
        gear = test.Store(TestGameSession.Metadata(11200001, type: 1));
        ItemMetadata dustData = TestGameSession.Metadata(40300001, type: 2);
        dust = test.Store(dustData with { Property = dustData.Property with { Tag = ItemTag.DustBlue } }, amount: 100);
        bagGem = test.Store(TestGameSession.Metadata(40200001, type: 13), amount: 3);
        test.Register(TestGameSession.Metadata(40200002, type: 13));
        GameSession session = test.Start();
        test.TakeSent();
        gear = session.Item.Inventory.Get(gear.Uid)!;
        bagGem = session.Item.Inventory.Get(bagGem.Uid, InventoryType.Gemstone)!;
        gear.Socket = new ItemSocket(3, 1);
        gear.Socket.Sockets[0] = new ItemGemstone(40200001, stats: new ItemStats());
        return session;
    }

    private void Send(GameSession session, byte command) {
        new ItemSocketHandler { ItemMetadata = null!, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(gear.Uid);
            writer.WriteByte(0);
            writer.WriteLong(77);
        }));
    }

    [Test]
    public void FailedRoll_KeepsTheGemAndCountsTheFail() {
        GameSession session = Start([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        ItemGemstone gem = gear.Socket!.Sockets[0]!;

        Send(session, 4);

        Assert.That(gear.Socket.Sockets[0]!.ItemId, Is.EqualTo(40200001));
        Assert.That(session.Item.Inventory.Get(dust.Uid)!.Amount, Is.EqualTo(90));
        Assert.That(test.TrophyCounter(23200028), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(23200029), Is.Null.Or.EqualTo(0));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.UpgradeGemstone(gear.Uid, 0, false, 77, gem).ToArray()) }));
    }

    // A gem in the inventory (item uid 0): a fail keeps the stack as it is, takes the ingredients and answers with the
    // gem and uid 0, from which the client finds the gem by its own uid and plays the fail.
    [Test]
    public void FailedRoll_InTheInventory_KeepsTheGem() {
        GameSession session = Start([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        new ItemSocketHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteLong(0);
            writer.WriteByte(0xFF);
            writer.WriteLong(bagGem.Uid);
        }));

        Item? kept = session.Item.Inventory.Get(bagGem.Uid, InventoryType.Gemstone);
        Assert.That(kept, Is.Not.Null);
        Assert.That(kept!.Id, Is.EqualTo(40200001));
        Assert.That(kept.Amount, Is.EqualTo(3));
        Assert.That(session.Item.Inventory.Get(dust.Uid)!.Amount, Is.EqualTo(90));
        Assert.That(test.TrophyCounter(23200028), Is.EqualTo(1));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.UpgradeGemstone(0, false, kept).ToArray()) }));
    }

    // An outfit gem (equipPart 4) rolls the skin list through the handler: gear list 0, skin list 100.
    [Test]
    public void OutfitGem_RollsTheSkinList() {
        GameSession session = Start([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], equipPart: 4, skinRates: [0, 0, 100, 100, 100, 100, 100, 100, 100, 100, 100]);

        Send(session, 4);

        Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200002));
        Assert.That(test.TrophyCounter(23200029), Is.EqualTo(1));
    }

    [Test]
    public void SureRoll_Upgrades() {
        GameSession session = Start([0, 0, 100, 100, 100, 100, 100, 100, 100, 100, 100]);

        Send(session, 4);

        Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200002));
        Assert.That(test.TrophyCounter(23200029), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(23200028), Is.Null.Or.EqualTo(0));
    }

    // The stage answer carries the rate the upgrade rolls (KR: 25 for a level 1 gem).
    [Test]
    public void Stage_SendsTheRate() {
        GameSession session = Start(KrRates);

        Send(session, 6);

        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.StageUpgradeGemstone(gear.Uid, 0, 77, 25f).ToArray()) }));
    }

    [TestCase(1, 3, 25)]
    [TestCase(9, 3, 8)]
    [TestCase(1, 4, 80)]
    [TestCase(5, 4, 20)]
    [TestCase(10, 3, 100)]
    public void Rate_ByTheLevelItGives(int level, int equipPart, int rate) {
        Assert.That(ItemSocketRules.GemstoneUpgradeRate(level, equipPart, KrRates, KrSkinRates), Is.EqualTo(rate));
    }

    // An ingredient named by id counts only copies of its rank's rarity (FUN_140f6e580 -> inventory.cpp:782-803): a rank 2
    // ingredient held at rarity 3 refuses the upgrade, held at rarity 2 it is taken (a stackable held at 1 now loads at
    // its rank, Item.LoadedRarity).
    [TestCase(3, false)]
    [TestCase(2, true)]
    public void IdIngredient_CountsOnlyItsRankRarity(int heldRarity, bool upgrades) {
        ItemMetadata stoneData = TestGameSession.Metadata(40300002, type: 2);
        stoneData = stoneData with { Property = stoneData.Property with { Rank = 2 } };
        GameSession session = Start([0, 0, 100, 100, 100, 100, 100, 100, 100, 100, 100], more: new GemstoneUpgradeTable.Ingredient(ItemTag.None, 1, 40300002));
        test.Register(stoneData);
        Item stone = new Item(stoneData, heldRarity, 1);
        Assert.That(session.Item.Inventory.Add(stone, commit: true), Is.True);

        Send(session, 4);

        Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(upgrades ? 40200002 : 40200001));
        Assert.That(session.Item.Inventory.Get(dust.Uid)!.Amount, Is.EqualTo(upgrades ? 90 : 100));
        Assert.That(session.Item.Inventory.Filter(item => item.Id == 40300002).Sum(item => item.Amount), Is.EqualTo(upgrades ? 0 : 1));
    }

    private static readonly int[] SureRates = [0, 0, 100, 100, 100, 100, 100, 100, 100, 100, 100];

    // A KR gem: rank 4, its option constant its own id (no rarity).
    private static ItemMetadata RankFourGem(int id, int type = 13) {
        ItemMetadata data = TestGameSession.Metadata(id, type: type);
        return data with {
            Property = data.Property with { Rank = 4 },
            Option = new ItemMetadataOption(0, default, 0, default, id, default, 0, 0),
        };
    }

    // A gem made with no rarity named (a drop, a reward row of grade 0) is made at its rank (Item.DefaultRarity,
    // GetItemRank itemdata.cpp:2084), so the upgrade that takes it by id and rank counts it. It was made at rarity 1
    // (its option constant is no rarity), and the upgrade answered lack.
    [Test]
    public void IdIngredient_MadeWithNoRarity_IsCounted() {
        GameSession session = Start(SureRates, more: new GemstoneUpgradeTable.Ingredient(ItemTag.None, 1, 40220131));
        test.Register(RankFourGem(40220131));
        // CreateItem rolls the item's options; the gem has none here.
        test.SetTable(new ItemOptionPickTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>()));
        test.SetTable(new ItemOptionConstantTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>()));
        test.SetTable(new ItemOptionStaticTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        Item made = session.Field!.ItemDrop.CreateItem(40220131)!;
        Assert.That(made.Rarity, Is.EqualTo(4));
        Assert.That(session.Item.Inventory.Add(made, commit: true), Is.True);

        Send(session, 4);

        Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200002));
        Assert.That(session.Item.Inventory.Filter(item => item.Id == 40220131).Sum(item => item.Amount), Is.EqualTo(0));
    }

    // A copy the character cannot use (PC::CanUseItem, pc.cpp:4683: bound to another character) is not counted,
    // as the client's counts skip it (inventory.cpp:798, 845): the upgrade is refused and nothing is taken.
    [Test]
    public void BoundToAnotherCharacter_IsNotCounted() {
        GameSession session = Start(SureRates);
        Item held = session.Item.Inventory.Get(dust.Uid)!;
        held.Transfer = new ItemTransfer(binding: new ItemBinding(test.CharacterId + 1, "other"));

        Send(session, 4);

        Assert.That(gear.Socket!.Sockets[0]!.ItemId, Is.EqualTo(40200001));
        Assert.That(session.Item.Inventory.Get(dust.Uid)!.Amount, Is.EqualTo(100));
    }

    [Test]
    public void Rate_NoListIsSure() {
        Assert.That(ItemSocketRules.GemstoneUpgradeRate(1, 3, null, null), Is.EqualTo(100));
        Assert.That(ItemSocketRules.GemstoneUpgradeSucceeds(25, 24), Is.True);
        Assert.That(ItemSocketRules.GemstoneUpgradeSucceeds(25, 25), Is.False);
    }
}

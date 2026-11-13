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

    private GameSession Start(int[] rates, int equipPart = 3, int[]? skinRates = null) {
        test = new TestGameSession();
        test.SetTable(new GemstoneUpgradeTable(new Dictionary<int, GemstoneUpgradeTable.Entry> {
            [40200001] = new(1, 40200002, [new GemstoneUpgradeTable.Ingredient(ItemTag.DustBlue, 10)], equipPart),
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

    [Test]
    public void Rate_NoListIsSure() {
        Assert.That(ItemSocketRules.GemstoneUpgradeRate(1, 3, null, null), Is.EqualTo(100));
        Assert.That(ItemSocketRules.GemstoneUpgradeSucceeds(25, 24), Is.True);
        Assert.That(ItemSocketRules.GemstoneUpgradeSucceeds(25, 25), Is.False);
    }
}

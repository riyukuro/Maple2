using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The treasure map appraise (UIItemTreasureMapDialog), on a map shaped like KR item 30001025: the parameter names a
// paid and a free drop box. The dialog asks the cost (TreasureMap mode 2), then uses the item with "free" or no text.
public class TreasureMapHandlerTests {
    private const int MapItemId = 30001025;
    private const int PaidReward = 30001026;
    private const int FreeReward = 20000756;
    private const int SecondFreeReward = 20000757;
    private const int PaidBox = 330001748;
    private const int FreeBox = 330001824;
    private const string Parameters = "<v groupId=\"1\" dropId=\"330001748\" freeDropId=\"330001824\" />";

    private TestGameSession test = null!;
    private Item map = null!;

    private GameSession Login(string parameters = Parameters, int amount = 2, ItemFunction function = ItemFunction.TreasureMap,
                              bool twoFreeGroups = false, short? miscSlots = null) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(PaidReward), TestGameSession.Metadata(FreeReward), TestGameSession.Metadata(SecondFreeReward));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [PaidBox] = TestGameSession.DropBox([(PaidReward, 1)]),
            [FreeBox] = twoFreeGroups
                ? TestGameSession.DropBox([(FreeReward, 1)], [(SecondFreeReward, 2)])
                : TestGameSession.DropBox([(FreeReward, 1)]),
        }));
        if (miscSlots != null) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [miscSlots.Value, 0] });
        }
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        map = test.Store(TestGameSession.Metadata(MapItemId, function: new ItemMetadataFunction(function, function.ToString(), parameters, false)), amount: amount);
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session, string text) {
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(map.Uid);
            writer.WriteUnicodeString(text);
        }));
    }

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    private static string Hex(ByteWriter writer) => Convert.ToHexString(writer.ToArray());

    [TestCase(false)]
    [TestCase(true)]
    public void CostRequest_AnswersTheUidAndTheCost(bool free) {
        GameSession session = Login();

        new TreasureMapHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(map.Uid);
            writer.WriteBool(free);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        var reader = new ByteReader(sent[0], 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.ItemScript));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(map.Uid));
        Assert.That(reader.ReadLong(), Is.EqualTo(free ? 0 : TreasureMapRules.AppraiseCost));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // An item that is no treasure map, or a map without the box asked for, reads "cannot be appraised" (cost -1).
    [Test]
    public void CostRequest_ForAnItemWithoutABox_AnswersMinusOne() {
        GameSession session = Login(parameters: "<v groupId=\"1\" dropId=\"330001748\" />");
        var handler = new TreasureMapHandler();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(map.Uid);
            writer.WriteBool(true);
        }));
        Assert.That(Convert.ToHexString(test.TakeSent().Single()), Is.EqualTo(Hex(ItemScriptPacket.TreasureMapCost(map.Uid, -1))));

        session = Login(function: ItemFunction.OpenItemBox);
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(map.Uid);
            writer.WriteBool(false);
        }));
        Assert.That(Convert.ToHexString(test.TakeSent().Single()), Is.EqualTo(Hex(ItemScriptPacket.TreasureMapCost(map.Uid, -1))));
    }

    [Test]
    public void CostRequest_ForAnItemNotHeldOrAnotherMode_SendsNothing() {
        GameSession session = Login();
        var handler = new TreasureMapHandler();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(map.Uid + 1000);
            writer.WriteBool(false);
        }));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(map.Uid);
            writer.WriteBool(false);
        }));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [TestCase("", PaidReward)]
    [TestCase("free", FreeReward)]
    public void Appraise_TakesOneMapAndGivesTheBoxRoll(string text, int reward) {
        GameSession session = Login();

        Use(session, text);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.UpdateAmount(map.Uid, 1))));
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ItemScriptPacket.TreasureMapResult(reward, 1))));
        Assert.That(Held(session, MapItemId), Is.EqualTo(1));
        Assert.That(Held(session, reward), Is.EqualTo(1));
        Assert.That(Held(session, reward == PaidReward ? FreeReward : PaidReward), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // A box of more than one group (the free box 330001824 in the KR data): every group's roll is given, the answer
    // shows the first.
    [Test]
    public void Appraise_GivesTheRollOfEveryGroup() {
        GameSession session = Login(twoFreeGroups: true);

        Use(session, "free");

        Assert.That(Convert.ToHexString(test.TakeSent()[^1]), Is.EqualTo(Hex(ItemScriptPacket.TreasureMapResult(FreeReward, 1))));
        Assert.That(Held(session, MapItemId), Is.EqualTo(1));
        Assert.That(Held(session, FreeReward), Is.EqualTo(1));
        Assert.That(Held(session, SecondFreeReward), Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    // A full bag (the misc tab holds the maps alone): the map is kept and nothing is given.
    [Test]
    public void Appraise_WithAFullBag_KeepsTheMap() {
        GameSession session = Login(miscSlots: 1);
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(0));

        Use(session, "");

        Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)), Is.EqualTo(new[] { Hex(ItemScriptPacket.TreasureMapFailed()) }));
        Assert.That(Held(session, MapItemId), Is.EqualTo(2));
        Assert.That(Held(session, PaidReward), Is.EqualTo(0));
    }

    // Room for one of the two groups' items: refused whole, the map is kept.
    [Test]
    public void Appraise_WithRoomForPartOfTheRoll_KeepsTheMap() {
        GameSession session = Login(twoFreeGroups: true, miscSlots: 2);
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(1));

        Use(session, "free");

        Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)), Is.EqualTo(new[] { Hex(ItemScriptPacket.TreasureMapFailed()) }));
        Assert.That(Held(session, MapItemId), Is.EqualTo(2));
        Assert.That(Held(session, FreeReward) + Held(session, SecondFreeReward), Is.EqualTo(0));
    }

    [Test]
    public void ResultPacket_Layout() {
        var reader = new ByteReader(ItemScriptPacket.TreasureMapResult(PaidReward, 3).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.ItemScript));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(PaidReward));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));

        Assert.That(Convert.ToHexString(ItemScriptPacket.TreasureMapFailed().ToArray()), Is.EqualTo("F5000300"));
    }

    // A map without the box of the appraise asked for keeps the map; the dialog goes back to its list.
    [Test]
    public void Appraise_WithoutTheBox_KeepsTheMap() {
        GameSession session = Login(parameters: "<v groupId=\"1\" dropId=\"330001748\" />");

        Use(session, "free");

        Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)), Is.EqualTo(new[] { Hex(ItemScriptPacket.TreasureMapFailed()) }));
        Assert.That(Held(session, MapItemId), Is.EqualTo(2));
    }

    [Test]
    public void Appraise_WhenTheBoxGivesNothing_KeepsTheMap() {
        GameSession session = Login(parameters: "<v groupId=\"1\" dropId=\"5\" freeDropId=\"330001824\" />");

        Use(session, "");

        Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)), Is.EqualTo(new[] { Hex(ItemScriptPacket.TreasureMapFailed()) }));
        Assert.That(Held(session, MapItemId), Is.EqualTo(2));
    }

    [TestCase(Parameters, false, PaidBox)]
    [TestCase(Parameters, true, FreeBox)]
    [TestCase("<v groupId=\"1\" freeDropId=\"7\" />", true, 7)]
    public void Rules_PickTheBox(string parameters, bool free, int expected) {
        Assert.That(TreasureMapRules.TryPick(parameters, free, out int boxId, out long cost), Is.True);
        Assert.That(boxId, Is.EqualTo(expected));
        Assert.That(cost, Is.EqualTo(free ? 0 : TreasureMapRules.AppraiseCost));
    }

    [TestCase(null, false)]
    [TestCase("", true)]
    [TestCase("<v groupId=\"1\" freeDropId=\"7\" />", false)]
    [TestCase("<v groupId=\"1\" dropId=\"x\" />", false)]
    public void Rules_NoBox(string? parameters, bool free) {
        Assert.That(TreasureMapRules.TryPick(parameters, free, out _, out _), Is.False);
    }
}

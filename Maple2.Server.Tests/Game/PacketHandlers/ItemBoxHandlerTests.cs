using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The three box functions with the most KMS2 uses (OpenItemBox, SelectItemBox, OpenGachaBox), through
// ItemUseHandler.Handle (a use from the bag) and ItemBoxHandler.Handle (RequestItemBox, the multi-open dialog).
public class ItemBoxHandlerTests {
    private const int BoxId = 20300001;
    private const int RewardA = 20000001;
    private const int RewardB = 20000002;
    private const int DropBoxId = 100;
    private const int GachaInfoId = 300;

    private TestGameSession test = null!;
    private Item box = null!;

    // The box in the bag, and drop box 100 = two groups: RewardA x3 | RewardB x1.
    private GameSession Login(ItemFunction function, string parameters, int amount, int rewardSlotMax = 100, short[]? tabCount = null) {
        test = new TestGameSession();
        ItemMetadata boxMetadata = TestGameSession.Metadata(BoxId, function: new ItemMetadataFunction(function, function.ToString(), parameters, false));
        test.Register(TestGameSession.Metadata(RewardA, slotMax: rewardSlotMax), TestGameSession.Metadata(RewardB, slotMax: rewardSlotMax));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [DropBoxId] = function == ItemFunction.SelectItemBox
                ? TestGameSession.DropBox([(RewardA, 3), (RewardB, 1)])
                : TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        test.SetTable(new GachaInfoTable(new Dictionary<int, GachaInfoTable.Entry> { [GachaInfoId] = new(0, DropBoxId, 0, 0, 0) }));
        if (tabCount != null) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = tabCount });
        }
        box = test.Store(boxMetadata, amount: amount);
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session, string? text = null) {
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(box.Uid);
            if (text != null) {
                writer.WriteUnicodeString(text);
            }
        }));
    }

    private static void MultiOpen(GameSession session, int count, string selection = "") {
        new ItemBoxHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(BoxId);
            writer.WriteShort();
            writer.WriteInt(count);
            writer.WriteUnicodeString(selection);
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    private int Mails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.Count(mail => mail.ReceiverId == test.CharacterId);
    }

    // KMS2 answers a box used from the bag with the inventory packets alone: the box first, then what it gave.
    [Test]
    public void OpenItemBox_Use_TakesOneBoxThenAddsItsItems() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2);

        Use(session);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.UpdateAmount(box.Uid, 1))));
        Assert.That(sent.Select(TestGameSession.Op).Distinct(), Is.EqualTo(new[] { SendOp.ItemInventory }));
        Assert.That(Held(session, BoxId), Is.EqualTo(1));
        Assert.That(Held(session, RewardA), Is.EqualTo(3));
        Assert.That(Held(session, RewardB), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void OpenItemBox_Use_TheLastBoxIsRemovedFirst() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 1);

        Use(session);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.Remove(box.Uid))));
        Assert.That(sent.Select(TestGameSession.Op).Distinct(), Is.EqualTo(new[] { SendOp.ItemInventory }));
        Assert.That(session.Item.Inventory.Get(box.Uid), Is.Null);
        Assert.That(Held(session, RewardA), Is.EqualTo(3));
    }

    // A box that needs more of itself than the bag holds stays, with no answer.
    [Test]
    public void OpenItemBox_Use_NeedsItsRequiredAmount() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId},5", amount: 4);

        Use(session);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session, BoxId), Is.EqualTo(4));
        Assert.That(Held(session, RewardA), Is.EqualTo(0));
    }

    // RequestItemBox: the boxes are opened one after another and ItemBox ends the answer with the count opened.
    [Test]
    public void OpenItemBox_MultiOpen_AnswersWithTheCount() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 5);

        MultiOpen(session, 3);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ItemBoxPacket.Open(BoxId, 3, ItemBoxError.ok))));
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.ItemBox), Is.EqualTo(1));
        Assert.That(Held(session, BoxId), Is.EqualTo(2));
        Assert.That(Held(session, RewardA), Is.EqualTo(9));
        Assert.That(Held(session, RewardB), Is.EqualTo(3));
    }

    [TestCase(0)]
    [TestCase(21)]
    public void OpenItemBox_MultiOpen_RefusesACountTheClientCannotSend(int count) {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 30);

        MultiOpen(session, count);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session, BoxId), Is.EqualTo(30));
    }

    // More boxes asked than held: the ones held are opened; the answer still ends with ItemBox.
    [Test]
    public void OpenItemBox_MultiOpen_StopsWhenTheBoxesRunOut() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2);

        MultiOpen(session, 5);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ItemBoxPacket.Open(BoxId, 2, ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail))));
        Assert.That(Held(session, BoxId), Is.EqualTo(0));
        Assert.That(Held(session, RewardA), Is.EqualTo(6));
    }

    // A full bag, as KMS2: the item that does not fit goes to the mail, the box that gave it counts as opened and
    // the multi-open stops with error 4.
    [Test]
    public void OpenItemBox_MultiOpen_FullBagMailsTheItemAndStops() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 5, rewardSlotMax: 3, tabCount: [2, 0]);
        Assert.That(box.Inventory, Is.EqualTo(InventoryType.Misc));
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(1));

        MultiOpen(session, 3);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ItemBoxPacket.Open(BoxId, 1, ItemBoxError.s_err_cannot_open_multi_itembox_inventory))));
        // The box, the item that fits (Add, NotifyNew), the full-bag error for the one that does not, then ItemBox.
        Assert.That(sent.Select(packet => $"{TestGameSession.Op(packet)}:{packet[2]}"), Is.EqualTo(new[] {
            "ItemInventory:2", "ItemInventory:0", "ItemInventory:7", "ItemInventory:13", "ItemBox:" + sent[^1][2],
        }));
        Assert.That(Held(session, BoxId), Is.EqualTo(4));
        Assert.That(Held(session, RewardA), Is.EqualTo(3));
        Assert.That(Held(session, RewardB), Is.EqualTo(0));
        Assert.That(Mails(), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // The select box dialog sends the picked row; the box gives that row only.
    [Test]
    public void SelectItemBox_Use_GivesThePickedRow() {
        GameSession session = Login(ItemFunction.SelectItemBox, $"1,{DropBoxId}", amount: 2);

        Use(session, "1");

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.UpdateAmount(box.Uid, 1))));
        Assert.That(sent.Select(TestGameSession.Op).Distinct(), Is.EqualTo(new[] { SendOp.ItemInventory }));
        Assert.That(Held(session, RewardA), Is.EqualTo(0));
        Assert.That(Held(session, RewardB), Is.EqualTo(1));
    }

    [TestCase("2")]    // not a row of the list
    [TestCase("0,1")]  // more rows than the box lets pick
    [TestCase("")]
    public void SelectItemBox_Use_KeepsTheBoxForABadPick(string selection) {
        GameSession session = Login(ItemFunction.SelectItemBox, $"1,{DropBoxId}", amount: 2);

        Use(session, selection);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session, BoxId), Is.EqualTo(2));
        Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.EqualTo(0));
    }

    [Test]
    public void SelectItemBox_MultiOpen_GivesThePickedRowPerBox() {
        GameSession session = Login(ItemFunction.SelectItemBox, $"1,{DropBoxId}", amount: 4);

        MultiOpen(session, 4, "0");

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ItemBoxPacket.Open(BoxId, 4, ItemBoxError.ok))));
        Assert.That(Held(session, BoxId), Is.EqualTo(0));
        Assert.That(Held(session, RewardA), Is.EqualTo(12));
        Assert.That(Held(session, RewardB), Is.EqualTo(0));
    }

    // A gacha box, as KMS2: the box, the items, then the gacha script that plays the result; no ItemBox.
    [TestCase("", 1)]
    [TestCase("multi", 10)]
    public void OpenGachaBox_Use_EndsWithTheGachaScript(string text, int opened) {
        GameSession session = Login(ItemFunction.OpenGachaBox, $"{GachaInfoId}", amount: 12);

        Use(session, text);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.UpdateAmount(box.Uid, 11))));
        Assert.That(TestGameSession.Op(sent[^1]), Is.EqualTo(SendOp.ItemScript));
        Assert.That(BitConverter.ToInt32(sent[^1], 3), Is.EqualTo(opened * 2));
        Assert.That(sent.Select(TestGameSession.Op).Distinct(), Is.EquivalentTo(new[] { SendOp.ItemInventory, SendOp.ItemScript }));
        Assert.That(Held(session, BoxId), Is.EqualTo(12 - opened));
        Assert.That(Held(session, RewardA), Is.EqualTo(3 * opened));
        Assert.That(session.Item.Inventory.Find(RewardA).All(item => item.GachaDismantleId == GachaInfoId), Is.True);
    }

    [Test]
    public void OpenGachaBox_Use_WithoutItsGachaRowKeepsTheBox() {
        GameSession session = Login(ItemFunction.OpenGachaBox, "999", amount: 1);

        Use(session, "");

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session, BoxId), Is.EqualTo(1));
    }

    // Two uses of the last box: the second finds no box and gives nothing.
    [Test]
    public void OpenItemBox_Use_TwiceWithOneBoxOpensOnce() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 1);
        Use(session);
        test.TakeSent();

        Use(session);
        MultiOpen(session, 1);

        Assert.That(test.TakeSentOps(), Has.No.Member(SendOp.ItemInventory));
        Assert.That(Held(session, RewardA), Is.EqualTo(3));
        Assert.That(Held(session, RewardB), Is.EqualTo(1));
    }
}

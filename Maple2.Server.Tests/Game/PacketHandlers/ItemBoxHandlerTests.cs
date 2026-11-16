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
using Maple2.Server.Tests.Validators;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The three box functions with the most KMS2 uses (OpenItemBox, SelectItemBox, OpenGachaBox), through
// ItemUseHandler.Handle (a use from the bag) and ItemBoxHandler.Handle (RequestItemBox, the multi-open dialog).
public class ItemBoxHandlerTests {
    private const int BoxId = 20300001;
    private const int RewardA = 20000001;
    private const int RewardB = 20000002;
    private const int DropBoxId = 100;
    private const int GachaInfoId = 300;
    private const int KeyId = 20300002;
    private const int OtherRarity = 4;

    private TestGameSession test = null!;
    private Item box = null!;
    private Item otherBox = null!;

    // The box in the bag, and drop box 100 = two groups: RewardA x3 | RewardB x1.
    private GameSession Login(ItemFunction function, string parameters, int amount, int rewardSlotMax = 100, short[]? tabCount = null,
                              int otherRarityAmount = 0, int keys = 0, bool requireVip = false,
                              Func<ItemMetadataLimit, ItemMetadataLimit>? limit = null, int rewardA = RewardA, int lockedAmount = 0,
                              GlobalDropItemBoxTable? globalTable = null) {
        test = new TestGameSession();
        ItemMetadata boxMetadata = TestGameSession.Metadata(BoxId, function: new ItemMetadataFunction(function, function.ToString(), parameters, false));
        if (requireVip) {
            boxMetadata = boxMetadata with { Limit = boxMetadata.Limit with { RequireVip = true } };
        }
        if (limit != null) {
            boxMetadata = boxMetadata with { Limit = limit(boxMetadata.Limit) };
        }
        test.Register(TestGameSession.Metadata(rewardA, slotMax: rewardSlotMax), TestGameSession.Metadata(RewardB, slotMax: rewardSlotMax));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [DropBoxId] = function == ItemFunction.SelectItemBox
                ? TestGameSession.DropBox([(rewardA, 3), (RewardB, 1)])
                : TestGameSession.DropBox([(rewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(globalTable ?? new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        test.SetTable(new GachaInfoTable(new Dictionary<int, GachaInfoTable.Entry> { [GachaInfoId] = new(0, DropBoxId, 0, 0, 0) }));
        if (tabCount != null) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = tabCount });
        }
        box = test.Store(boxMetadata, amount: amount);
        if (otherRarityAmount > 0) {
            // A second stack of the same box at another rarity (the box is stored at rarity 1).
            using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
            otherBox = db.CreateItem(test.CharacterId, new Item(boxMetadata, rarity: OtherRarity, amount: otherRarityAmount))!;
        }
        if (keys > 0) {
            test.Store(TestGameSession.Metadata(KeyId), amount: keys);
        }
        if (lockedAmount > 0) {
            // A second, locked stack of the box.
            using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
            db.CreateItem(test.CharacterId, new Item(boxMetadata, amount: lockedAmount) { IsLocked = true });
        }
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

    // The dialog sends the box's id and rarity (UIItemMultiOpenBoxDialog::vfunc_15).
    private void MultiOpen(GameSession session, int count, string selection = "") {
        new ItemBoxHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(BoxId);
            writer.WriteShort((short) box.Rarity);
            writer.WriteInt(count);
            writer.WriteUnicodeString(selection);
        }));
    }

    private void MultiOpen(GameSession session, int count, short rarity) {
        new ItemBoxHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(BoxId);
            writer.WriteShort(rarity);
            writer.WriteInt(count);
            writer.WriteUnicodeString("");
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    private static int Held(GameSession session, Item stack) => session.Item.Inventory.Get(stack.Uid)?.Amount ?? 0;

    private int Mails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.Count(mail => mail.ReceiverId == test.CharacterId);
    }

    // The dialog's request for an item that is no multi-open box (a name change voucher, a gacha box, a timed box) is
    // dropped: nothing is opened or answered. Before, the voucher's text parameters threw in int.Parse, which ended
    // the session, and the gacha box opened.
    [TestCase(ItemFunction.ChangeCharName, "name")]
    [TestCase(ItemFunction.OpenGachaBox, "300")]
    [TestCase(ItemFunction.OpenItemBoxLimitedQuantity, "<v cooldownTime=\"3\" individualDropBoxId=\"100\"/>")]
    public void MultiOpen_OfAnItemThatIsNoBoxIsDropped(ItemFunction function, string parameters) {
        GameSession session = Login(function, parameters, amount: 2);

        Assert.DoesNotThrow(() => MultiOpen(session, 1));

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session, box), Is.EqualTo(2));
        Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.EqualTo(0));
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
        // The box is stored taken before its items are given, so a crash after the open cannot open it again.
        Assert.That(test.Row(box.Uid)?.Amount, Is.EqualTo(1), "the box row is stored lowered at the open");
    }

    // The fixed-item shape of OpenItemBox, "global,1,item,0" (11 boxes in the data pack, which the played client's
    // box replaces for the mission boxes, ClientItemBoxes): no individual box, so no individual table row. It was refused before its item was given (no answer, the
    // box kept); now the box is taken and gives its item.
    [Test]
    public void OpenItemBox_Use_FixedItemWithNoDropBox_GivesTheItem() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,1,{RewardB},0", amount: 1);

        Use(session);

        Assert.That(Held(session, BoxId), Is.EqualTo(0));
        Assert.That(Held(session, RewardB), Is.EqualTo(1));
        Assert.That(Held(session, RewardA), Is.EqualTo(0));
    }

    // A global box whose rows are banded by level (the mission level-up box 30003: an experience potion or voucher per
    // 10 levels) gives the opener's band. Before, the open passed level 0, under every band's minLevel, so the box was
    // taken and gave nothing.
    [Test]
    public void OpenItemBox_Use_GlobalBoxGivesTheOpenersLevelBand() {
        const int globalBoxId = 30003, groupId = 80000001;
        var group = new GlobalDropItemBoxTable.Group(groupId, 0, 0, [new GlobalDropItemBoxTable.Group.DropCount(1, 100)], false, (MapType) 0, (Continent) 0);
        var global = new GlobalDropItemBoxTable(
            new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> { [globalBoxId] = new() { [groupId] = [group] } },
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>> {
                [groupId] = [
                    new GlobalDropItemBoxTable.Item(RewardA, 1, 9, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 10000, [], false),
                    new GlobalDropItemBoxTable.Item(RewardB, 10, 19, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 10000, [], false),
                ],
            });
        GameSession session = Login(ItemFunction.OpenItemBox, $"{globalBoxId},1,0,0", amount: 1, globalTable: global);
        session.Player.Value.Character.Level = 12;

        Use(session);

        Assert.That(Held(session, BoxId), Is.EqualTo(0));
        Assert.That(Held(session, RewardB), Is.EqualTo(1));
        Assert.That(Held(session, RewardA), Is.EqualTo(0));
    }

    // A fixed item the data has no row for, with no drop box: the box gives nothing, so it is kept.
    [Test]
    public void OpenItemBox_Use_UnknownFixedItemWithNoDropBox_KeepsTheBox() {
        GameSession session = Login(ItemFunction.OpenItemBox, "0,1,29999999,0", amount: 1);

        Use(session);

        Assert.That(Held(session, BoxId), Is.EqualTo(1));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The item box meso rate (SpecialAttribute.ItemBoxRewardMeso, +100% here) raises the mesos a box gives; its
    // items are unchanged.
    [Test]
    public void OpenItemBox_Use_ItemBoxMesoRateRaisesTheMesos() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2, rewardA: 90000001);
        session.Player.Stats.Values[SpecialAttribute.ItemBoxRewardMeso].AddTotal(1000);
        long mesoBefore = session.Currency.Meso;

        Use(session);

        Assert.That(session.Currency.Meso - mesoBefore, Is.EqualTo(6));
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
        Assert.That(test.Row(box.Uid)?.OwnerId, Is.EqualTo(0), "the last box's row is stored discarded at the open");
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

    // RequestItemBox names the box by id and rarity: no stack of that rarity, nothing is opened or sent.
    [Test]
    public void OpenItemBox_MultiOpen_OtherRarityOpensNothing() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 5);

        MultiOpen(session, 3, (short) (box.Rarity + 1));

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session, BoxId), Is.EqualTo(5));
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
        Assert.That(test.Row(box.Uid)?.Amount, Is.EqualTo(2), "each opened box is stored taken");
    }

    // RequestItemBox runs the item use checks (ItemUseHandler.UseAllowed): a box that expired while the dialog ran is
    // not opened, and the answer "Unable to open boxes" (3) closes the dialog.
    [Test]
    public void OpenItemBox_MultiOpen_ExpiredBoxIsRefused() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 5);
        session.Item.Inventory.Get(box.Uid)!.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;

        MultiOpen(session, 3);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString).ToArray(),
            Is.EqualTo(new[] { Hex(ItemBoxPacket.Open(BoxId, 0, ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail)) }));
        Assert.That(Held(session, box), Is.EqualTo(5));
        Assert.That(Held(session, RewardA), Is.EqualTo(0));
    }

    // A premium club item is opened only with premium club, as it is used only with it.
    [Test]
    public void OpenItemBox_MultiOpen_PremiumBoxNeedsPremiumClub() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 5, requireVip: true);
        session.Player.Value.Account.PremiumTime = 0;

        MultiOpen(session, 3);

        Assert.That(Convert.ToHexString(test.TakeSent().Single()),
            Is.EqualTo(Hex(ItemBoxPacket.Open(BoxId, 0, ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail))));
        Assert.That(Held(session, BoxId), Is.EqualTo(5));
    }

    // A box the player's gender, job or level may not use stays shut (GetItemRequirementError); a beginner counts as
    // a knight, and a job list naming the beginner allows every job.
    [TestCase(Gender.Female, 0, new JobCode[0], false)]
    [TestCase(Gender.All, 0, new[] { JobCode.Wizard }, false)]
    [TestCase(Gender.All, 2, new JobCode[0], false)]
    [TestCase(Gender.Male, 1, new[] { JobCode.Knight }, true)]
    [TestCase(Gender.All, 0, new[] { JobCode.Newbie, JobCode.Wizard }, true)]
    public void OpenItemBox_UseNeedsTheBoxRequirements(Gender gender, int level, JobCode[] jobs, bool opens) {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2,
            limit: old => old with { Gender = gender, Level = level, JobLimits = jobs });
        session.Player.Value.Character.Level = 1;

        Use(session);

        Assert.That(Held(session, BoxId), Is.EqualTo(opens ? 1 : 2));
    }

    // A dead player's client sends no item use but an OpenItemBox, OpenItemBoxLullu or OpenItemBoxLulluSimple one
    // (Unk_Inventory::RequestUseItem); a box with a key is not one of them.
    [TestCase(false)]
    [TestCase(true)]
    public void DeadPlayer_OpensOnlyThePlainBox(bool withKey) {
        GameSession session = withKey
            ? Login(ItemFunction.OpenItemBoxWithKey, $"{KeyId},1,0,0,0,{DropBoxId}", amount: 2, keys: 2)
            : Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2);
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session.Player, true);

        Use(session);

        Assert.That(Held(session, BoxId), Is.EqualTo(withKey ? 2 : 1));
    }

    [TestCase(MaritalStatus.Single, false)]
    [TestCase(MaritalStatus.Engaged, false)]
    [TestCase(MaritalStatus.Divorced, false)]
    [TestCase(MaritalStatus.Married, true)]
    [TestCase(MaritalStatus.DivorceFiledByGroom, true)]
    [TestCase(MaritalStatus.DivorceFiledByBride, true)]
    public void WeddingBox_OpensOnlyWhileMarried(MaritalStatus status, bool opens) {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2,
            limit: old => old with { RequireWedding = true });
        session.Marriage.Marriage.Status = status;

        Use(session);

        Assert.That(Held(session, BoxId), Is.EqualTo(opens ? 1 : 2));
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
        Assert.That(test.Row(box.Uid)?.Amount, Is.EqualTo(1), "the box row is stored lowered at the open");
    }

    // KR select box 20301130 carries "1,,330001622": the client skips the empty part (select count 1, its box).
    [Test]
    public void SelectItemBox_Use_EmptyParameterPartIsSkipped() {
        GameSession session = Login(ItemFunction.SelectItemBox, $"1,,{DropBoxId}", amount: 2);

        Use(session, "1");

        Assert.That(Held(session, BoxId), Is.EqualTo(1));
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
        Assert.That(test.Row(box.Uid)?.Amount, Is.EqualTo(12 - opened), "each capsule is stored taken at its open");
    }

    // A capsule naming a machine item (KeyId here): with GachaCapsuleMachine on it opens only with as many machines as
    // capsules, which stay in the bag; off, the machine is not asked.
    [TestCase(true, 1, 1)]
    [TestCase(true, 0, 0)]
    [TestCase(false, 0, 1)]
    public void OpenGachaBox_Machine_FollowsTheFeature(bool featureOn, int machines, int opened) {
        GameSession session = Login(ItemFunction.OpenGachaBox, $"{GachaInfoId},{KeyId}", amount: 2, keys: machines);
        if (!featureOn) {
            test.SetFeaturesOff(Maple2.Server.Game.Manager.ItemBoxManager.CapsuleMachineFeature);
        }

        Use(session, "");

        Assert.Multiple(() => {
            Assert.That(Held(session, BoxId), Is.EqualTo(2 - opened));
            Assert.That(Held(session, RewardA), Is.EqualTo(3 * opened));
            Assert.That(Held(session, KeyId), Is.EqualTo(machines));
        });
    }

    // RewardA x3 at SlotMax 1 drops as three items of one (ItemDropTable::DropIndividualGroup splits a count by
    // slotMax); with the second one's write failing it is mailed, two are in the bag, and the gacha script still
    // shows all three beside RewardB.
    [Test]
    public void OpenGachaBox_Use_FailedWriteMidwayShowsWhatTheCapsuleGave() {
        GameSession session = Login(ItemFunction.OpenGachaBox, $"{GachaInfoId}", amount: 2, rewardSlotMax: 1);
        int writes = 0;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == Microsoft.EntityFrameworkCore.EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item") && ++writes == 2;

        Use(session, "");
        test.FailSave = null;

        byte[] script = test.TakeSent().Last();
        Assert.Multiple(() => {
            Assert.That(TestGameSession.Op(script), Is.EqualTo(SendOp.ItemScript));
            Assert.That(BitConverter.ToInt32(script, 3), Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(script, 7), Is.EqualTo(RewardA));
            Assert.That(BitConverter.ToInt32(script, 11), Is.EqualTo(1));
            Assert.That(Held(session, RewardA), Is.EqualTo(2));
            Assert.That(Mails(), Is.EqualTo(1));
        });
    }

    // The client opens ten capsules only with ten in the bag, locked ones counted; nine are not opened one by one up
    // to the shortfall (before, the nine were opened and the open stopped at the tenth). With nine spendable and one
    // locked the client starts, and the nine spendable ones are opened.
    [TestCase(9, 0, 0)]
    [TestCase(10, 0, 10)]
    [TestCase(9, 1, 9)]
    public void OpenGachaBox_Multi_NeedsTenHeld(int held, int locked, int opened) {
        GameSession session = Login(ItemFunction.OpenGachaBox, $"{GachaInfoId}", amount: held, lockedAmount: locked);

        Use(session, "multi");

        Assert.Multiple(() => {
            Assert.That(Held(session, BoxId), Is.EqualTo(held + locked - opened));
            Assert.That(Held(session, RewardA), Is.EqualTo(3 * opened));
            if (opened == 0) {
                Assert.That(test.TakeSent(), Is.Empty);
            }
        });
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

    // Two uses of the last capsule: the second finds no capsule and gives nothing, and the capsule's row is gone.
    [Test]
    public void OpenGachaBox_Use_TwiceWithOneCapsuleOpensOnce() {
        GameSession session = Login(ItemFunction.OpenGachaBox, $"{GachaInfoId}", amount: 1);
        Use(session, "");
        test.TakeSent();

        Use(session, "");

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Has.No.Member(SendOp.ItemInventory));
            Assert.That(Held(session, BoxId), Is.EqualTo(0));
            Assert.That(Held(session, RewardA), Is.EqualTo(3));
            Assert.That(test.Row(box.Uid)?.OwnerId ?? 0, Is.EqualTo(0), "the capsule's row is no longer the character's");
        });
    }

    // Two stacks of one select box at two rarities: the dialog of one opens only that stack, whichever the bag
    // lists first.
    [TestCase(OtherRarity, 1, 4, 0)]
    [TestCase(1, 2, 2, 1)]
    public void SelectItemBox_MultiOpen_UsesOnlyThePickedRarity(int rarity, int count, int leftRarity1, int leftOther) {
        GameSession session = Login(ItemFunction.SelectItemBox, $"1,{DropBoxId}", amount: 4, otherRarityAmount: 1);

        new ItemBoxHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(BoxId);
            writer.WriteShort((short) rarity);
            writer.WriteInt(count);
            writer.WriteUnicodeString("0");
        }));

        Assert.That(Held(session, box), Is.EqualTo(leftRarity1));
        Assert.That(Held(session, otherBox), Is.EqualTo(leftOther));
        Assert.That(Held(session, RewardA), Is.EqualTo(3 * count));
    }

    // The same for a box with a key: the key is any stack, the box only the picked rarity's.
    [TestCase(OtherRarity, 1, 4, 0)]
    [TestCase(1, 2, 2, 1)]
    public void OpenItemBoxWithKey_MultiOpen_UsesOnlyThePickedRarity(int rarity, int count, int leftRarity1, int leftOther) {
        GameSession session = Login(ItemFunction.OpenItemBoxWithKey, $"{KeyId},1,0,0,0,{DropBoxId}", amount: 4, otherRarityAmount: 1, keys: 5);

        new ItemBoxHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(BoxId);
            writer.WriteShort((short) rarity);
            writer.WriteInt(count);
            writer.WriteUnicodeString("");
        }));

        Assert.That(Held(session, box), Is.EqualTo(leftRarity1));
        Assert.That(Held(session, otherBox), Is.EqualTo(leftOther));
        Assert.That(Held(session, KeyId), Is.EqualTo(5 - count));
    }

    // A Lulu box bought for mesos (no box in the bag, autoPay): a spend on another thread while the key leaves the
    // bag (a trade takes every meso left at the key's packet). The price was taken before the key, so the box is
    // paid and opened. Before, the price was taken after the key; that take threw on the empty balance, and the
    // key was gone with nothing given.
    [Test]
    public void OpenItemBoxLullu_ASpendWhileTheKeyLeavesCannotSkipThePrice() {
        const int price = 300;
        test = new TestGameSession();
        string parameters = $"<v commonBoxId=\"{DropBoxId}\" unCommonBoxId=\"{DropBoxId}\" boxItemTag=\"LulluBox\" keyItemTag=\"LulluKey\" boxPrice=\"{price}\" />";
        ItemMetadata keyMetadata = TestGameSession.Metadata(KeyId, function: new ItemMetadataFunction(ItemFunction.OpenItemBoxLullu, "OpenItemBoxLullu", parameters, false));
        keyMetadata = keyMetadata with { Property = keyMetadata.Property with { Tag = ItemTag.LulluKey } };
        test.Register(TestGameSession.Metadata(RewardA), TestGameSession.Metadata(RewardB));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [DropBoxId] = TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        Item key = test.Store(keyMetadata, amount: 2);
        GameSession session = test.Start();
        session.Currency.Meso = price + 50;
        test.TakeSent();
        long spent = -1;
        test.OnSend = packet => {
            if (spent >= 0 || TestGameSession.Op(packet) != SendOp.ItemInventory) {
                return;
            }
            long left = session.Currency.Meso;
            spent = session.Currency.TryTakeMeso(left, notify: false) ? left : 0;
        };

        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(key.Uid);
            writer.WriteUnicodeString("autoPay");
        }));

        Assert.That(spent, Is.EqualTo(50));
        Assert.That(session.Currency.Meso, Is.EqualTo(0));
        Assert.That(Held(session, KeyId), Is.EqualTo(1));
        Assert.That(test.Row(key.Uid)?.Amount, Is.EqualTo(1), "the key is stored taken at the open");
        Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.GreaterThan(0));
    }

    // A simple Lulu box (OpenItemBoxLulluSimple) and a pet box (OpenPetBox) take one item of their tag per open and
    // store the take before the box's items are given (ConsumeNow).
    [TestCase(ItemFunction.OpenItemBoxLulluSimple, "keyItemTag")]
    [TestCase(ItemFunction.OpenPetBox, "itemTag")]
    public void TaggedBox_StoresTheTakeAtTheOpen(ItemFunction function, string tagParameter) {
        test = new TestGameSession();
        string parameters = function == ItemFunction.OpenPetBox
            ? $"<v {tagParameter}=\"LulluKey\" unCommonBoxId=\"{DropBoxId}\" />"
            : $"<v commonBoxId=\"{DropBoxId}\" {tagParameter}=\"LulluKey\" />";
        ItemMetadata keyMetadata = TestGameSession.Metadata(KeyId, function: new ItemMetadataFunction(function, function.ToString(), parameters, false));
        keyMetadata = keyMetadata with { Property = keyMetadata.Property with { Tag = ItemTag.LulluKey } };
        test.Register(TestGameSession.Metadata(RewardA), TestGameSession.Metadata(RewardB));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [DropBoxId] = TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        Item key = test.Store(keyMetadata, amount: 2);
        GameSession session = test.Start();
        test.TakeSent();

        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(key.Uid);
            writer.WriteUnicodeString("");
        }));

        Assert.Multiple(() => {
            Assert.That(Held(session, KeyId), Is.EqualTo(1));
            Assert.That(test.Row(key.Uid)?.Amount, Is.EqualTo(1), "the take is stored at the open");
            Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.GreaterThan(0));
        });
    }

    // 110001024 (here on this test's box, the KR row names 30001133): a box of its item ids opens a second time on
    // a 10 % roll, the box taken once.
    [TestCase(0, 2)]
    [TestCase(0.999, 1)]
    public void OpenItemBox_ItemBoxEffectOpensItAgain(double roll, int times) {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2);
        SpecialEffectWiringTests.Hold(session.Player, SpecialEffectRulesTests.PrestigeBox with { ItemIds = [BoxId] }, roll);

        Use(session);

        Assert.That(Held(session, BoxId), Is.EqualTo(1));
        Assert.That(Held(session, RewardA), Is.EqualTo(3 * times));
        Assert.That(Held(session, RewardB), Is.EqualTo(times));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void OpenItemBox_ItemBoxEffectOfAnotherBox() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 2);
        SpecialEffectWiringTests.Hold(session.Player, SpecialEffectRulesTests.PrestigeBox);

        Use(session);

        Assert.That(Held(session, RewardA), Is.EqualTo(3));
    }

    // KR key boxes (OpenItemBoxWithKey, 20300133 "30000512,1,0,0,0,330000499": key 30000512 x1, drop box 330000499).
    // Without the key, or with a drop box the data lacks, the box is kept and nothing is given.
    [TestCase(0, DropBoxId)]
    [TestCase(1, 999)]
    public void OpenItemBoxWithKey_Use_WithoutTheKeyOrTheBoxRowKeepsTheBox(int keys, int dropBox) {
        GameSession session = Login(ItemFunction.OpenItemBoxWithKey, $"{KeyId},1,0,0,0,{dropBox}", amount: 2, keys: keys);

        Use(session);

        Assert.Multiple(() => {
            Assert.That(Held(session, BoxId), Is.EqualTo(2));
            Assert.That(Held(session, KeyId), Is.EqualTo(keys));
            Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.EqualTo(0));
            Assert.That(test.Row(box.Uid)?.Amount, Is.EqualTo(2));
        });
    }

    // With the key: the box and the key are taken, the box's groups are given.
    [Test]
    public void OpenItemBoxWithKey_Use_TakesTheBoxAndTheKey() {
        GameSession session = Login(ItemFunction.OpenItemBoxWithKey, $"{KeyId},1,0,0,0,{DropBoxId}", amount: 2, keys: 3);

        Use(session);

        Assert.Multiple(() => {
            Assert.That(Held(session, BoxId), Is.EqualTo(1));
            Assert.That(Held(session, KeyId), Is.EqualTo(2));
            Assert.That(Held(session, RewardA), Is.EqualTo(3));
            Assert.That(Held(session, RewardB), Is.EqualTo(1));
        });
    }

    // A select box (KR 20000679 "1,330001596") whose picked row does not fit: the box is taken, the row goes to the
    // mail, and the open answers the full-bag error, as the OpenItemBox multi-open does.
    [Test]
    public void SelectItemBox_Use_FullBagMailsThePickedRow() {
        GameSession session = Login(ItemFunction.SelectItemBox, $"1,{DropBoxId}", amount: 2, tabCount: [1, 0]);
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(0));

        Use(session, "1");

        Assert.Multiple(() => {
            Assert.That(Held(session, BoxId), Is.EqualTo(1));
            Assert.That(Held(session, RewardB), Is.EqualTo(0));
            Assert.That(Mails(), Is.EqualTo(1));
            Assert.That(test.Row(box.Uid)?.Amount, Is.EqualTo(1));
        });
    }

    // A select box naming a drop box the data lacks keeps the box.
    [Test]
    public void SelectItemBox_Use_WithoutItsBoxRowKeepsTheBox() {
        GameSession session = Login(ItemFunction.SelectItemBox, "1,999", amount: 2);

        Use(session, "0");

        Assert.That(Held(session, BoxId), Is.EqualTo(2));
        Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.EqualTo(0));
    }

    private const int LulluBoxId = 20300525;

    // A KR Lulu key (20300524: commonBoxId, unCommonBoxId, keyItemTag LulluKey, boxItemTag LulluBox, boxPrice 1000)
    // with the given keys, Lulu boxes in the bag and mesos.
    private (GameSession Session, Item Key) LoginLullu(int keys, int boxes, long meso, int dropBox = DropBoxId) {
        test = new TestGameSession();
        string parameters = $"<v commonBoxId=\"{dropBox}\" unCommonBoxId=\"{dropBox}\" keyItemTag=\"LulluKey\" boxItemTag=\"LulluBox\" boxPrice=\"1000\" meratMarketPopup=\"key\"/>";
        ItemMetadata keyMetadata = TestGameSession.Metadata(KeyId, function: new ItemMetadataFunction(ItemFunction.OpenItemBoxLullu, "OpenItemBoxLullu", parameters, false));
        keyMetadata = keyMetadata with { Property = keyMetadata.Property with { Tag = ItemTag.LulluKey } };
        ItemMetadata boxMetadata = TestGameSession.Metadata(LulluBoxId);
        boxMetadata = boxMetadata with { Property = boxMetadata.Property with { Tag = ItemTag.LulluBox } };
        test.Register(TestGameSession.Metadata(RewardA), TestGameSession.Metadata(RewardB));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [DropBoxId] = TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        Item key = test.Store(keyMetadata, amount: keys);
        if (boxes > 0) {
            test.Store(boxMetadata, amount: boxes);
        }
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        test.TakeSent();
        return (session, key);
    }

    private void UseLullu(GameSession session, Item key, string text) {
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(key.Uid);
            writer.WriteUnicodeString(text);
        }));
    }

    // A Lulu box in the bag pays for the open: the key and the box are taken, the mesos stay, and both drop boxes give.
    [Test]
    public void OpenItemBoxLullu_ABoxInTheBag_TakesTheKeyAndTheBox() {
        (GameSession session, Item key) = LoginLullu(keys: 2, boxes: 2, meso: 5000);

        UseLullu(session, key, "autoPay");

        Assert.Multiple(() => {
            Assert.That(Held(session, KeyId), Is.EqualTo(1));
            Assert.That(Held(session, LulluBoxId), Is.EqualTo(1));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(Held(session, RewardA), Is.EqualTo(6));
            Assert.That(Held(session, RewardB), Is.EqualTo(2));
        });
    }

    // No box in the bag: without autoPay, or with too few mesos for the price, nothing is taken or given and the open
    // answers error 4; with both, the price is paid.
    [TestCase("", 5000L, 5000L, false)]
    [TestCase("autoPay", 999L, 999L, false)]
    [TestCase("autoPay", 1000L, 0L, true)]
    public void OpenItemBoxLullu_NoBox_PaysThePriceOnlyWithAutoPay(string text, long meso, long mesoLeft, bool opens) {
        (GameSession session, Item key) = LoginLullu(keys: 2, boxes: 0, meso: meso);

        UseLullu(session, key, text);

        Assert.Multiple(() => {
            Assert.That(Held(session, KeyId), Is.EqualTo(opens ? 1 : 2));
            Assert.That(session.Currency.Meso, Is.EqualTo(mesoLeft));
            Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.EqualTo(opens ? 8 : 0));
            Assert.That(Convert.ToHexString(test.TakeSent()[^1]), Is.EqualTo(Hex(ItemBoxPacket.Open(KeyId, opens ? 1 : 0,
                opens ? ItemBoxError.ok : ItemBoxError.s_err_cannot_open_multi_itembox_inventory))));
        });
    }

    // A Lulu key whose drop boxes the data lacks rolls nothing: the key, the box and the mesos stay.
    [Test]
    public void OpenItemBoxLullu_EmptyRollTakesNothing() {
        (GameSession session, Item key) = LoginLullu(keys: 2, boxes: 2, meso: 5000, dropBox: 999);

        UseLullu(session, key, "");

        Assert.Multiple(() => {
            Assert.That(Held(session, KeyId), Is.EqualTo(2));
            Assert.That(Held(session, LulluBoxId), Is.EqualTo(2));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });
    }

    // A pet box rolls its unCommonBoxId boxes (shown as special) and its commonBoxId boxes; with neither, or without
    // its itemTag, it is refused and keeps the item.
    [TestCase("<v itemTag=\"LulluKey\" unCommonBoxId=\"100\" commonBoxId=\"100\" />", 1, 8)]
    [TestCase("<v itemTag=\"LulluKey\" />", 2, 0)]
    [TestCase("<v unCommonBoxId=\"100\" />", 2, 0)]
    public void OpenPetBox_RollsBothLists_OrIsRefused(string parameters, int left, int given) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(KeyId, function: new ItemMetadataFunction(ItemFunction.OpenPetBox, "OpenPetBox", parameters, false));
        metadata = metadata with { Property = metadata.Property with { Tag = ItemTag.LulluKey } };
        test.Register(TestGameSession.Metadata(RewardA), TestGameSession.Metadata(RewardB));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [DropBoxId] = TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        Item key = test.Store(metadata, amount: 2);
        GameSession session = test.Start();
        test.TakeSent();

        UseLullu(session, key, "");

        Assert.Multiple(() => {
            Assert.That(Held(session, KeyId), Is.EqualTo(left));
            Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.EqualTo(given));
        });
    }

    // A transcendence crystal (group 3 type 70) on its KR shape, 37000013: <function name="OpenItemBox"
    // parameter="<v globalDropLevel="0" globalDropRank="1" globalDropBoxId="0" individualDropBoxId="330002090"
    // notifyBox="s_msg_item_open_item_box_mergeitem"/>" />. One crystal opens its individual box.
    [Test]
    public void TranscendenceCrystal_OpensItsIndividualBox() {
        const int crystalId = 37000013;
        string parameters = $"<v globalDropLevel=\"0\" globalDropRank=\"1\" globalDropBoxId=\"0\" individualDropBoxId=\"{DropBoxId}\" notifyBox=\"s_msg_item_open_item_box_mergeitem\"/>";
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardA), TestGameSession.Metadata(RewardB));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [DropBoxId] = TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        box = test.Store(TestGameSession.Metadata(crystalId, function: new ItemMetadataFunction(ItemFunction.OpenItemBox, "OpenItemBox", parameters, false)), amount: 2);
        GameSession session = test.Start();
        test.TakeSent();
        Assert.That(box.Type.IsTranscendenceCrystal, Is.True);

        Use(session);

        Assert.Multiple(() => {
            Assert.That(Held(session, crystalId), Is.EqualTo(1));
            Assert.That(Held(session, RewardA), Is.EqualTo(3));
            Assert.That(Held(session, RewardB), Is.EqualTo(1));
        });
    }

    // A drop row with isAnnounce is announced to the world with a link to the stack that took the item, the box's id
    // in the line; the row without it stays quiet.
    [Test]
    public void AnnouncedRow_IsAnnouncedWithTheItemsLink() {
        GameSession session = Login(ItemFunction.OpenItemBox, $"0,0,0,{DropBoxId}", amount: 1);
        IDictionary<int, IndividualDropItemTable.Entry> rows = TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]);
        rows[1] = rows[1] with { Items = [rows[1].Items[0] with { Announce = true }] };
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> { [DropBoxId] = rows }));

        Use(session);

        Item reward = session.Item.Inventory.Find(RewardA).Single();
        Assert.Multiple(() => {
            Assert.That(test.World.Chats, Has.Count.EqualTo(1));
            Assert.That(test.World.Chats.Single().ItemUids, Is.EqualTo(new[] { reward.Uid }));
            Assert.That(test.World.Chats.Single().Message, Is.EqualTo(Maple2.Server.Game.Util.ChatUtil.ItemBoxAnnouncement(reward.Uid, session.PlayerName, BoxId)));
            Assert.That(Held(session, RewardB), Is.EqualTo(1));
        });
    }

    // KR 37000013 (a transcendence crystal box, item type 3/70): OpenItemBox with the xml parameters
    // globalDropLevel 0, globalDropRank 1, globalDropBoxId 0, individualDropBoxId 330002090, notifyBox. One use takes
    // the box and gives the individual box's items; the notice name is not a drop box.
    [Test]
    public void OpenItemBox_TranscendenceCrystalBox_OpensItsIndividualBox() {
        const int crystalBoxId = 37000013;
        const int dropBoxId = 330002090;
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(crystalBoxId, function: new ItemMetadataFunction(ItemFunction.OpenItemBox, "OpenItemBox",
            "<v globalDropLevel=\"0\" globalDropRank=\"1\" globalDropBoxId=\"0\" individualDropBoxId=\"330002090\" notifyBox=\"s_msg_item_open_item_box_mergeitem\"/>", false));
        test.Register(TestGameSession.Metadata(RewardA), TestGameSession.Metadata(RewardB));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [dropBoxId] = TestGameSession.DropBox([(RewardA, 3)], [(RewardB, 1)]),
        }));
        test.SetServerTable(new GlobalDropItemBoxTable(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(),
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>()));
        box = test.Store(metadata, amount: 2);
        GameSession session = test.Start();
        test.TakeSent();
        Assert.That(session.Item.Inventory.Get(box.Uid)!.Type.IsTranscendenceCrystal, Is.True);

        Use(session);

        Assert.Multiple(() => {
            Assert.That(Held(session, box), Is.EqualTo(1));
            Assert.That(Held(session, RewardA), Is.EqualTo(3));
            Assert.That(Held(session, RewardB), Is.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}

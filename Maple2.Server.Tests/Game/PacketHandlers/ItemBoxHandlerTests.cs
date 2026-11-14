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
                              Func<ItemMetadataLimit, ItemMetadataLimit>? limit = null) {
        test = new TestGameSession();
        ItemMetadata boxMetadata = TestGameSession.Metadata(BoxId, function: new ItemMetadataFunction(function, function.ToString(), parameters, false));
        if (requireVip) {
            boxMetadata = boxMetadata with { Limit = boxMetadata.Limit with { RequireVip = true } };
        }
        if (limit != null) {
            boxMetadata = boxMetadata with { Limit = limit(boxMetadata.Limit) };
        }
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
        if (otherRarityAmount > 0) {
            // A second stack of the same box at another rarity (the box is stored at rarity 1).
            using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
            otherBox = db.CreateItem(test.CharacterId, new Item(boxMetadata, rarity: OtherRarity, amount: otherRarityAmount))!;
        }
        if (keys > 0) {
            test.Store(TestGameSession.Metadata(KeyId), amount: keys);
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

    // RewardA x3 at SlotMax 1 goes in as three pieces; with the second piece's write failing, one is in the bag and
    // the rest is mailed, and the gacha script still shows the 3 the capsule gave (it showed the mailed 2).
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
            Assert.That(BitConverter.ToInt32(script, 3), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt32(script, 7), Is.EqualTo(RewardA));
            Assert.That(BitConverter.ToInt32(script, 11), Is.EqualTo(3));
            Assert.That(Held(session, RewardA), Is.EqualTo(1));
            Assert.That(Mails(), Is.EqualTo(1));
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
        Assert.That(Held(session, RewardA) + Held(session, RewardB), Is.GreaterThan(0));
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
}

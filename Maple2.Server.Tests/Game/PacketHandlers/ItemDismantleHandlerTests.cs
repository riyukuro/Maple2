using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The dismantle dialog for gear without a break table row, as KMS2 answers it: onyx by the sell price and
// the chaos onyx row, listed at 0 too.
public class ItemDismantleHandlerTests {
    private const int Onyx = 40100023;
    private const int ChaosOnyx = 40100024;

    private TestGameSession test = null!;
    private ItemDismantleHandler handler = null!;
    private Item gear = null!;

    // box: the donation box was used first (the client opens the type 2 dialog only from it).
    private GameSession Login(int rarity, EquipSlot slot = EquipSlot.CL, byte dialog = 1, int itemType = 1, bool box = true,
        params AchievementMetadata[] trophies) => Login(rarity, slot, dialog, itemType, box, null, trophies);

    private GameSession Login(int rarity, EquipSlot slot, byte dialog, int itemType, bool box, Func<ItemMetadata, ItemMetadata>? edit,
        params AchievementMetadata[] trophies) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(11400001, slot, type: itemType);
        metadata = metadata with { Property = metadata.Property with { SellPrices = [100, 200, 7139, 9000, 0, 0] } };
        metadata = edit?.Invoke(metadata) ?? metadata;
        gear = test.Store(metadata);
        test.Register(TestGameSession.Metadata(Onyx, type: 4, slotMax: 9999), TestGameSession.Metadata(ChaosOnyx, type: 4, slotMax: 9999));
        test.SetTable(new ItemBreakTable(new Dictionary<int, IReadOnlyList<ItemBreakTable.Ingredient>>()));
        test.SetTable(new GachaInfoTable(new Dictionary<int, GachaInfoTable.Entry>()));
        test.SetServerTable(new BonusGameTable(new Dictionary<int, BonusGameTable.Game>(), new Dictionary<int, BonusGameTable.Drop>()));
        test.AddAchievement(trophies);
        GameSession session = test.Start();
        handler = new ItemDismantleHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };
        session.Item.Inventory.Get(gear.Uid)!.Rarity = rarity;
        if (dialog == 2 && box) {
            UseUiObject(session, Ui(DonationBox, ItemDismantleHandler.DonationDialog), "box");
        }
        Open(session, dialog);
        test.TakeSent();
        return session;
    }

    private void Open(GameSession session, byte dialog) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte(dialog);
        }));
    }

    private const int DonationBox = 10001313;

    private static InteractObjectMetadata Ui(int id, string swf) {
        return new InteractObjectMetadata(id, InteractType.Ui, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), [],
            new InteractObjectMetadataUi(swf, string.Empty));
    }

    private void UseUiObject(GameSession session, InteractObjectMetadata row, string entityId) {
        test.AddInteract(row, new InteractUiObject(entityId, new Ms2SimpleUiObject(row.Id, Vector3.Zero, Vector3.Zero)));
        new InteractObjectHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteInt(1);
            writer.WriteString(entityId);
        }));
        test.TakeSent();
    }

    private void Stage(GameSession session) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(-1);
            writer.WriteLong(gear.Uid);
            writer.WriteInt(1);
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [TestCase(3, EquipSlot.CL, 142, 0, 0)]
    [TestCase(4, EquipSlot.CL, 190, 3, 3)]
    [TestCase(4, EquipSlot.OH, 95, 1, 2)]
    public void Stage_ListsOnyxAndTheChaosOnyxRow(int rarity, EquipSlot slot, int onyx, int chaosMin, int chaosMax) {
        GameSession session = Login(rarity, slot);

        Stage(session);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemDismantlePacket.Stage(gear.Uid, 0, 1)),
            Hex(ItemDismantlePacket.Preview(new Dictionary<int, (int Min, int Max)> { [Onyx] = (onyx, onyx), [ChaosOnyx] = (chaosMin, chaosMax) })),
        }));
    }

    // CItemTransfer::CanBreak (itemsocketsystemutil.cpp:2993): a gacha item passes first; else enableBreak without the
    // no-dismantle flag, no PC bang item, and an item with a skin type only with SkinBreak on and, when it has a ucc mesh,
    // not with a socket of no opened slot.
    [TestCase(true, false, 0, false, false, true, false, ExpectedResult = true)]
    [TestCase(true, false, 0, true, false, true, false, ExpectedResult = false)]
    [TestCase(true, false, 0, false, true, true, false, ExpectedResult = true)]
    [TestCase(true, false, 0, false, true, false, false, ExpectedResult = false)]
    [TestCase(true, false, 0, false, true, true, true, ExpectedResult = false)]
    [TestCase(true, false, 0, false, false, false, true, ExpectedResult = true)]
    [TestCase(false, false, 7, true, true, false, true, ExpectedResult = true)]
    [TestCase(false, false, 0, false, false, true, false, ExpectedResult = false)]
    public bool Breakable_AsCanBreak(bool enableBreak, bool disableBreak, int gacha, bool pcBang, bool skin, bool skinBreakOn, bool closedTemplate) =>
        DismantleRules.Breakable(enableBreak, disableBreak, gacha, pcBang, skin, skinBreakOn, closedTemplate);

    // An outfit (skin type 1) is staged only with SkinBreak on; a PC bang item never.
    [TestCase(false, false, ExpectedResult = true)]
    [TestCase(true, false, ExpectedResult = false)]
    [TestCase(false, true, ExpectedResult = false)]
    public bool Stage_SkinNeedsSkinBreak_PcBangNever(bool skinBreakOff, bool pcBang) {
        GameSession session = Login(3, EquipSlot.CL, 1, 1, true, metadata => metadata with {
            Property = metadata.Property with { SkinType = 1 },
            Limit = metadata.Limit with { RequirePcBang = pcBang },
        });
        if (skinBreakOff) {
            test.SetFeaturesOff(ItemDismantleHandler.SkinBreakFeature);
        }
        GiveABreakRow(session);
        Stage(session);
        return test.TakeSent().Any(p => Convert.ToHexString(p).StartsWith(Hex(ItemDismantlePacket.Stage(gear.Uid, 0, 1))));
    }

    // The confirm: the item leaves the bag, the onyx comes in, and the result lists the chaos onyx with 0.
    [Test]
    public void Confirm_GivesOnyxAndReportsNoChaosOnyx() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.Remove(gear.Uid))));
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(ItemDismantlePacket.Result(1, new Dictionary<int, int> { [Onyx] = 142, [ChaosOnyx] = 0 }))));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Null);
        Assert.That(session.Item.Inventory.Find(Onyx).Sum(item => item.Amount), Is.EqualTo(142));
        Assert.That(session.Item.Inventory.Find(ChaosOnyx), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // Rarity 4 pays 3 chaos onyx: the listed max is reached.
    [Test]
    public void Confirm_PaysTheListedMax() {
        GameSession session = Login(rarity: 4);
        Stage(session);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(Convert.ToHexString(test.TakeSent()[^1]), Is.EqualTo(Hex(ItemDismantlePacket.Result(1, new Dictionary<int, int> { [Onyx] = 190, [ChaosOnyx] = 3 }))));
        Assert.That(session.Item.Inventory.Find(ChaosOnyx).Sum(item => item.Amount), Is.EqualTo(3));
    }

    // Part of a stack: 3 of 5 staged pay 3 times the break row, and 2 stay in the bag. The reward was read after
    // the consume had lowered the stack to 2, so it paid nothing.
    [Test]
    public void Confirm_OfPartOfAStackPaysTheStagedCount() {
        const int Stack = 30000001;
        GameSession session = Login(rarity: 3);
        Item stack = test.Store(TestGameSession.Metadata(Stack, type: 4, slotMax: 99), amount: 5);
        Assert.That(session.Item.Inventory.Add(stack, commit: true), Is.True);
        test.SetTable(new ItemBreakTable(new Dictionary<int, IReadOnlyList<ItemBreakTable.Ingredient>> {
            [Stack] = [new ItemBreakTable.Ingredient(Onyx, 2)],
        }));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(-1);
            writer.WriteLong(stack.Uid);
            writer.WriteInt(3);
        }));
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(Convert.ToHexString(test.TakeSent()[^1]), Is.EqualTo(Hex(ItemDismantlePacket.Result(1, new Dictionary<int, int> { [Onyx] = 6 }))));
        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(session.Item.Inventory.Find(Onyx).Sum(item => item.Amount), Is.EqualTo(6));
    }

    // A staged item cannot be staged again after an earlier slot was emptied: the duplicate check stopped at the
    // first empty slot, so the second stage took slot 0 and the preview counted the item twice.
    [Test]
    public void Stage_OfAStagedItemBehindAnEmptySlotIsRefused() {
        GameSession session = Login(rarity: 3);
        Item other = test.Store(TestGameSession.Metadata(11400002, EquipSlot.CL, type: 1));
        Assert.That(session.Item.Inventory.Add(other, commit: true), Is.True);
        Stage(session);
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(-1);
            writer.WriteLong(other.Uid);
            writer.WriteInt(1);
        }));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(gear.Uid);
        }));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(-1);
            writer.WriteLong(other.Uid);
            writer.WriteInt(1);
        }));

        Assert.That(session.DismantleStaging.Count(staged => staged.Uid == other.Uid), Is.EqualTo(1));
    }

    // Two confirms in a row: the second finds nothing staged, answers with result 5 and gives nothing more.
    [Test]
    public void Confirm_TwiceBreaksTheItemOnce() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.Error(5)) }));
        Assert.That(session.Item.Inventory.Find(Onyx).Sum(item => item.Amount), Is.EqualTo(142));
    }

    // The staged item left the bag before the confirm: result 5, nothing given.
    [Test]
    public void Confirm_OfAnItemThatIsGoneGivesNothing() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        Assert.That(session.Item.Inventory.Consume(gear.Uid, 1), Is.True);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.Error(5)) }));
        Assert.That(session.Item.Inventory.Find(Onyx), Is.Empty);
    }

    // A confirm with nothing staged, and a locked item: nothing is broken.
    [Test]
    public void Confirm_OfALockedItemKeepsIt() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        session.Item.Inventory.Get(gear.Uid)!.IsLocked = true;
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.Error(5)) }));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Find(Onyx), Is.Empty);
    }

    // An item still unlocking is refused as a locked one (CanBreakItem, CItem::FUN_140528e20): not staged, and
    // kept by a confirm when it was staged before its unlock began.
    [Test]
    public void UnlockingItem_IsNeitherStagedNorBroken() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        session.Item.Inventory.Get(gear.Uid)!.UnlockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.Error(5)) }));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Find(Onyx), Is.Empty);

        Stage(session);
        Assert.That(session.DismantleStaging.Any(staged => staged.Uid == gear.Uid), Is.False);
    }

    // A gemstone in a socket refuses the item (CItemSocket::CheckGemsBreakable): not staged, and a gem put in after
    // the stage keeps it at the confirm. The dismantle took the gems with it.
    [Test]
    public void ItemWithAGem_IsNeitherStagedNorBroken() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        Item item = session.Item.Inventory.Get(gear.Uid)!;
        item.Socket = new ItemSocket(1, 1);
        item.Socket.Sockets[0] = new ItemGemstone(40000001, stats: new ItemStats());
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Find(Onyx), Is.Empty);

        Stage(session);
        Assert.That(session.DismantleStaging.Any(staged => staged.Uid == gear.Uid), Is.False);
    }

    // A pet whose bag holds items: error 31 at the stage, and kept at the confirm.
    [Test]
    public void PetWithBagItems_IsNeitherStagedNorBroken() {
        GameSession session = Login(rarity: 3);
        Stage(session);
        session.Item.Inventory.Get(gear.Uid)!.Pet = new ItemPet { HasItems = true };
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
        test.TakeSent();

        Stage(session);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_cannot_destroy_petitem_hasitem)),
        }));
        Assert.That(session.DismantleStaging.Any(staged => staged.Uid == gear.Uid), Is.False);
    }

    // An outfit with no break table row and no script reward (skin type 6: a count of 0) is not staged
    // (CItemBreakManager::HasBreakReward); with a row it is.
    [TestCase(false, ExpectedResult = false)]
    [TestCase(true, ExpectedResult = true)]
    public bool Stage_OutfitNeedsABreakReward(bool row) {
        GameSession session = Login(3, EquipSlot.CL, 1, 1, true, metadata => metadata with {
            Property = metadata.Property with { SkinType = 6 },
        });
        if (row) {
            GiveABreakRow(session);
        }
        test.TakeSent();
        Stage(session);
        return session.DismantleStaging.Any(staged => staged.Uid == gear.Uid);
    }

    // A break table row for the gear (an outfit then has a reward in every locale).
    private void GiveABreakRow(GameSession session) {
        test.SetTable(new ItemBreakTable(new Dictionary<int, IReadOnlyList<ItemBreakTable.Ingredient>> {
            [gear.Id] = [new ItemBreakTable.Ingredient(Onyx, 1)],
        }));
        handler = new ItemDismantleHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };
        Open(session, 1);
        test.TakeSent();
    }

    // The Green Hoods donation box (dialog type 2, map 02000422 interact 10001313).
    private const int Philanthropist = 22500042; // donation_item
    private const int ArmingTheTroops = 93000071; // donation_type, code "1,0,0"
    private const int Dismantler = 74000011; // item_break

    private GameSession Donation(int itemType = 1, bool box = true) {
        return Login(rarity: 4, dialog: 2, itemType: itemType, box: box, trophies: [
            TestGameSession.Trophy(Philanthropist, ConditionType.donation_item, [100]),
            TestGameSession.Trophy(ArmingTheTroops, ConditionType.donation_type, [100], codes: [1, 0, 0]),
            TestGameSession.Trophy(Dismantler, ConditionType.item_break, [100]),
        ]);
    }

    // Staged in the donation box: the stage answer only, no reward preview (mode 5 goes to the break dialog only).
    [Test]
    public void Donation_StageSendsNoPreview() {
        GameSession session = Donation();

        Stage(session);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.Stage(gear.Uid, 0, 1)) }));
    }

    // The confirm takes the item and gives nothing: no onyx, an empty type-2 result, the donation conditions
    // counted and item_break not.
    [Test]
    public void Donation_ConfirmGivesNoRewardsAndCountsNoItemBreak() {
        GameSession session = Donation();
        Stage(session);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(ItemInventoryPacket.Remove(gear.Uid))));
        Assert.That(sent.Select(Convert.ToHexString), Does.Contain(Hex(ItemDismantlePacket.Result(2, new Dictionary<int, int>()))));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Null);
        Assert.That(session.Item.Inventory.Find(Onyx), Is.Empty);
        Assert.That(session.Item.Inventory.Find(ChaosOnyx), Is.Empty);
        Assert.That(test.TrophyCounter(Philanthropist), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(ArmingTheTroops), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(Dismantler), Is.Null);
        Assert.That(test.Errors, Is.Empty);
    }

    // The client opens the donation dialog only from the donation box (its Ui page): without a used box, or a second
    // time for one use, or with only another Ui object used, the open is taken as a close, so nothing is staged or donated.
    [TestCase(false, null)]
    [TestCase(true, "box again")]
    [TestCase(false, "RouletteDialog")]
    public void Donation_OpensOnlyFromTheDonationBox(bool box, string? before) {
        GameSession session = Donation(box: box);
        if (before == "box again") {
            Open(session, 0);
            Open(session, 2);
        } else if (before != null) {
            UseUiObject(session, Ui(31000001, before), "other");
            Open(session, 2);
        }
        test.TakeSent();

        Stage(session);
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(session.DismantleOpened, Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
        Assert.That(test.TrophyCounter(Philanthropist), Is.Null);
        Assert.That(test.TrophyCounter(ArmingTheTroops), Is.Null);
    }

    // An item of another type without a skin type (TryAddItem's 0x8f7, s_itembreak_invalid_donation_item):
    // not staged, and the confirm finds nothing, so nothing is taken or counted.
    [Test]
    public void Donation_RefusesANonDonationItem() {
        GameSession session = Donation(itemType: 2);

        Stage(session);
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.DismantleStaging, Is.All.EqualTo(default((long, int))));

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.Error(5)) }));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
        Assert.That(test.TrophyCounter(Philanthropist), Is.Null);
        Assert.That(test.TrophyCounter(ArmingTheTroops), Is.Null);
        Assert.That(test.TrophyCounter(Dismantler), Is.Null);
    }

    // A staged slot the client never sent (or one staged before the dialog type changed) is checked again at
    // the confirm: a non-donation item is dropped there and kept.
    [Test]
    public void Donation_ConfirmDropsANonDonationSlot() {
        GameSession session = Donation(itemType: 2);
        session.DismantleStaging[0] = (gear.Uid, 1);

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.Error(5)) }));
        Assert.That(session.Item.Inventory.Get(gear.Uid), Is.Not.Null);
        Assert.That(test.TrophyCounter(Philanthropist), Is.Null);
    }

    // Auto-add in the donation box skips the non-donation item.
    [Test]
    public void Donation_AutoAddSkipsANonDonationItem() {
        GameSession session = Donation(itemType: 2);

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteByte(InventoryType.Gear.ToClient());
            writer.WriteByte(6);
        }));

        Assert.That(session.DismantleStaging, Is.All.EqualTo(default((long, int))));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemDismantlePacket.StageList([])) }));
    }
}

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

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The dismantle dialog for gear without a break table row, as KMS2 answers it: onyx by the sell price and
// the chaos onyx row, listed at 0 too.
public class ItemDismantleHandlerTests {
    private const int Onyx = 40100023;
    private const int ChaosOnyx = 40100024;

    private TestGameSession test = null!;
    private ItemDismantleHandler handler = null!;
    private Item gear = null!;

    private GameSession Login(int rarity, EquipSlot slot = EquipSlot.CL, byte dialog = 1, int itemType = 1, params AchievementMetadata[] trophies) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(11400001, slot, type: itemType);
        metadata = metadata with { Property = metadata.Property with { SellPrices = [100, 200, 7139, 9000, 0, 0] } };
        gear = test.Store(metadata);
        test.Register(TestGameSession.Metadata(Onyx, type: 4, slotMax: 9999), TestGameSession.Metadata(ChaosOnyx, type: 4, slotMax: 9999));
        test.SetTable(new ItemBreakTable(new Dictionary<int, IReadOnlyList<ItemBreakTable.Ingredient>>()));
        test.SetTable(new GachaInfoTable(new Dictionary<int, GachaInfoTable.Entry>()));
        test.AddAchievement(trophies);
        GameSession session = test.Start();
        handler = new ItemDismantleHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata };
        session.Item.Inventory.Get(gear.Uid)!.Rarity = rarity;
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte(dialog);
        }));
        test.TakeSent();
        return session;
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

    // The Green Hoods donation box (dialog type 2, map 02000422 interact 10001313).
    private const int Philanthropist = 22500042; // donation_item
    private const int ArmingTheTroops = 93000071; // donation_type, code "1,0,0"
    private const int Dismantler = 74000011; // item_break

    private GameSession Donation(int itemType = 1) {
        return Login(rarity: 4, dialog: 2, itemType: itemType, trophies: [
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

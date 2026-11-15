using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// EnchantScroll 1 (preview) and 2 (enchant): what the client's scroll dialog gets for a scroll or an item that
// does not fit (the error list of OnEnchantScroll) and for the one that does.
public class EnchantScrollHandlerTests {
    private const int ScrollId = 20291001;
    private const int RowId = 7;
    private const int GearId = 11400001; // item type 14

    private TestGameSession test = null!;
    private GameSession session = null!;
    private Item scroll = null!;
    private Item gear = null!;

    [TearDown]
    public void TearDown() => session?.Dispose();

    private void Start(EnchantScrollMetadata row, int rarity = 1) {
        test = new TestGameSession();
        ItemMetadata scrollMetadata = TestGameSession.Metadata(ScrollId, function: new ItemMetadataFunction(ItemFunction.EnchantScroll, "EnchantScroll", RowId.ToString(), false));
        scroll = test.Store(scrollMetadata, amount: 3);
        gear = test.Store(TestGameSession.Metadata(GearId, EquipSlot.CL, type: 1), rarity: rarity);
        test.SetTable(new EnchantScrollTable(new Dictionary<int, EnchantScrollMetadata> { [RowId] = row }));
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        session = test.Start();
        test.TakeSent();
    }

    private static EnchantScrollMetadata Row(EnchantScrollType type = EnchantScrollType.Enchant, int[]? enchants = null, int[]? itemTypes = null,
                                             int[]? rarities = null, short minLevel = 0, short maxLevel = 50) {
        return new EnchantScrollMetadata(type, minLevel, maxLevel, enchants ?? [3], itemTypes ?? [14], rarities ?? [1]);
    }

    private void Send(byte mode, long scrollUid, long itemUid) {
        new EnchantScrollHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteLong(scrollUid);
            writer.WriteLong(itemUid);
        }));
    }

    // The scroll dialog's own packets: the bag's update of the spent scroll goes by another opcode.
    private string[] Sent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.EnchantScroll).Select(Convert.ToHexString).ToArray();
    private static string Error(EnchantScrollError error) => Convert.ToHexString(EnchantScrollPacket.Error(error).ToArray());
    private Item Held() => session.Item.GetGear(gear.Uid)!;

    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    public void UnknownScroll_AnswersInvalidScroll(byte mode) {
        Start(Row());

        Send(mode, long.MaxValue, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_scroll) }));
    }

    // The scroll row of an item that is not an enchant scroll (or whose row is not in the table) is no scroll.
    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    public void AnItemThatIsNoScroll_AnswersInvalidScroll(byte mode) {
        Start(Row());

        Send(mode, gear.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_scroll) }));
    }

    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    public void ARowOutsideTheTable_AnswersInvalidScroll(byte mode) {
        Start(Row());
        test.SetTable(new EnchantScrollTable(new Dictionary<int, EnchantScrollMetadata>()));

        Send(mode, scroll.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_scroll) }));
    }

    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    public void ARowWithoutGrades_AnswersInvalidScroll(byte mode) {
        Start(Row(enchants: []));

        Send(mode, scroll.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_scroll) }));
    }

    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    public void UnknownItem_AnswersInvalidItem(byte mode) {
        Start(Row());

        Send(mode, scroll.Uid, long.MaxValue);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_item) }));
    }

    [Test]
    public void ItemBelowTheLevelBand_AnswersInvalidLevel() {
        Start(Row(minLevel: 10));

        Send(1, scroll.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_level) }));
    }

    [Test]
    public void ItemOfAnotherType_AnswersInvalidSlot() {
        Start(Row(itemTypes: [30]));

        Send(1, scroll.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_slot) }));
    }

    [Test]
    public void ItemOfAnotherRarity_AnswersInvalidRank() {
        Start(Row(rarities: [3]));

        Send(1, scroll.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_rank) }));
    }

    [Test]
    public void ItemAtTheScrollsGrade_AnswersInvalidGrade() {
        Start(Row());
        Held().Enchant = new ItemEnchant { Enchants = 3 };

        Send(1, scroll.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_invalid_grade) }));
    }

    // A grade-0 scroll answers s_enchantscroll_not_breaking_item (the dialog's result 8, string 2937), not the grade
    // error every item would otherwise hit first.
    [Test]
    public void AScrollToGradeZero_AnswersNotBreakingItem() {
        Start(Row(EnchantScrollType.Restore, enchants: [0]));

        Send(1, scroll.Uid, gear.Uid);

        Assert.That(Sent(), Is.EqualTo(new[] { Error(EnchantScrollError.s_enchantscroll_not_breaking_item) }));
    }

    [TestCase(EnchantScrollType.Enchant)]
    [TestCase(EnchantScrollType.Random)]
    public void Preview_OfAFittingItem_AnswersThePreviewAndTakesNothing(EnchantScrollType type) {
        Start(Row(type, enchants: type == EnchantScrollType.Random ? [2, 3] : [3]));

        Send(1, scroll.Uid, gear.Uid);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.EnchantScroll));
        Assert.That(Convert.ToHexString(sent[0]), Is.Not.EqualTo(Error(EnchantScrollError.s_enchantscroll_invalid_item)));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)!.Amount, Is.EqualTo(3));
        Assert.That(Held().Enchant?.Enchants ?? 0, Is.EqualTo(0));
    }

    // A one-grade scroll sets the item to its grade, takes one scroll of the stack and answers the item.
    [Test]
    public void Enchant_OfAFittingItem_RaisesTheGradeAndTakesOneScroll() {
        Start(Row());

        Send(2, scroll.Uid, gear.Uid);

        Assert.That(Held().Enchant!.Enchants, Is.EqualTo(3));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(EnchantScrollPacket.Enchant(Held()).ToArray()) }));
    }

    // The roll never lowers an item: a random scroll under the item's grade keeps the grade (the scroll is spent).
    [Test]
    public void Enchant_RandomScrollBelowTheItemsGrade_KeepsTheGrade() {
        Start(Row(EnchantScrollType.Random, enchants: [1, 5]));
        Held().Enchant = new ItemEnchant { Enchants = 4 };

        Send(2, scroll.Uid, gear.Uid);

        Assert.That(Held().Enchant!.Enchants, Is.GreaterThanOrEqualTo(4));
        Assert.That(session.Item.Inventory.Get(scroll.Uid)!.Amount, Is.EqualTo(2));
    }

    [Test]
    public void UnknownMode_IsIgnored() {
        Start(Row());

        Send(3, scroll.Uid, gear.Uid);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(scroll.Uid)!.Amount, Is.EqualTo(3));
    }
}

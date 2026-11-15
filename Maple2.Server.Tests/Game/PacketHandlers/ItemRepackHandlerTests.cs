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

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The repacking scroll (ItemRepack 0x008C, mode 1: scroll uid, target uid) through the handler: the error the client
// shows for each refused case (CItemRePackingScrollTable::Check order) and the commit. Ids are made up.
public class ItemRepackHandlerTests {
    private const int ScrollItem = 20300901;
    private const int ScrollId = 9001;
    private const int Target = 11300901; // group 1, type 13

    private TestGameSession test = null!;
    private Item scroll = null!;
    private Item target = null!;

    private static ItemMetadata TargetData(Func<ItemMetadata, ItemMetadata>? tweak = null) {
        ItemMetadata data = TestGameSession.Metadata(Target);
        data = data with {
            Property = data.Property with { RepackCount = 2, RepackConsumeCount = 1 },
            Limit = data.Limit with { Level = 40, TransferType = TransferType.BindOnEquip },
        };
        return tweak?.Invoke(data) ?? data;
    }

    // Builds the session with one scroll stack of `scrollAmount` and one target.
    private void Build(Func<ItemMetadata, ItemMetadata>? tweak = null, int scrollAmount = 1, int rarity = 3,
                       ItemRepackingScrollMetadata? scrollData = null, string parameters = "9001") {
        test = new TestGameSession();
        var entry = scrollData ?? new ItemRepackingScrollMetadata(30, 50, [13], [3], false);
        test.SetTable(new ItemRepackingScrollTable(new Dictionary<int, ItemRepackingScrollMetadata> { [ScrollId] = entry }));
        test.Store(TestGameSession.Metadata(ScrollItem, function: new ItemMetadataFunction(ItemFunction.ItemRePackingScroll, "", parameters, false)), scrollAmount);
        test.Store(TargetData(tweak), 1, rarity: rarity);
        test.Start();
        scroll = test.Session.Item.Inventory.Find(ScrollItem).First();
        target = test.Session.Item.Inventory.Find(Target).First();
        test.TakeSent();
    }

    private byte[][] Repack(long scrollUid, long targetUid) {
        new ItemRepackHandler().Handle(test.Session, TestGameSession.Packet(w => {
            w.WriteByte(1);
            w.WriteLong(scrollUid);
            w.WriteLong(targetUid);
        }));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.ItemRepackage).ToArray();
    }

    private byte[][] Repack() => Repack(scroll.Uid, target.Uid);

    private static byte[] Error(ItemRepackError error) => ItemRepackPacket.Error(error).ToArray();

    [Test]
    public void Commit_RepacksTheItem_AndTakesTheScroll() {
        Build(scrollAmount: 2);

        byte[][] sent = Repack();

        Assert.Multiple(() => {
            Assert.That(sent, Is.EqualTo(new[] { ItemRepackPacket.Commit(target).ToArray() }));
            Assert.That(target.Transfer!.RemainTrades, Is.EqualTo(1));
            Assert.That(target.Transfer.RepackageCount, Is.EqualTo(1));
            Assert.That(test.Session.Item.Inventory.Find(ScrollItem).Sum(i => i.Amount), Is.EqualTo(1));
        });
    }

    [Test]
    public void Commit_LastScroll_RemovesTheStack() {
        Build();

        Repack();

        Assert.That(test.Session.Item.Inventory.Find(ScrollItem), Is.Empty);
    }

    [Test]
    public void Commit_TwiceThenNoRepackLeft_IsRefused() {
        Build(scrollAmount: 3);

        Repack();
        target.Transfer!.RemainTrades = 0;
        Repack();
        target.Transfer.RemainTrades = 0;
        byte[][] third = Repack();

        Assert.Multiple(() => {
            Assert.That(third, Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_remain_count) }));
            Assert.That(target.Transfer.RepackageCount, Is.EqualTo(2));
            Assert.That(test.Session.Item.Inventory.Find(ScrollItem).Sum(i => i.Amount), Is.EqualTo(1));
        });
    }

    [Test]
    public void Commit_ConsumeCountTwo_TakesTwoScrolls() {
        Build(data => data with { Property = data.Property with { RepackConsumeCount = 2 } }, scrollAmount: 5);

        Repack();

        Assert.That(test.Session.Item.Inventory.Find(ScrollItem).Sum(i => i.Amount), Is.EqualTo(3));
    }

    [Test]
    public void Commit_ConsumeCountTwo_OneScroll_IsRefused() {
        Build(data => data with { Property = data.Property with { RepackConsumeCount = 2 } }, scrollAmount: 1);

        byte[][] sent = Repack();

        Assert.Multiple(() => {
            Assert.That(sent, Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_server_fail_consume_scroll) }));
            Assert.That(target.Transfer!.RepackageCount, Is.EqualTo(0));
            Assert.That(test.Session.Item.Inventory.Find(ScrollItem).Sum(i => i.Amount), Is.EqualTo(1));
        });
    }

    [Test]
    public void UnknownScrollUid_IsInvalidScroll() {
        Build();

        Assert.That(Repack(scroll.Uid + 999, target.Uid), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_invalid_scroll) }));
    }

    [Test]
    public void ScrollThatIsNotARepackScroll_IsInvalidScroll() {
        Build();

        Assert.That(Repack(target.Uid, target.Uid), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_invalid_scroll) }));
    }

    [Test]
    public void UnknownTargetUid_IsInvalidTarget() {
        Build();

        Assert.That(Repack(scroll.Uid, target.Uid + 999), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target) }));
    }

    [TestCase("abc")]
    [TestCase("")]
    [TestCase("12345")]
    public void ScrollWithoutATableRow_IsInvalidScroll(string parameters) {
        Build(parameters: parameters);

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_invalid_scroll) }));
    }

    [Test]
    public void ExpiredTarget_Answers2() {
        Build();
        target.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;

        Assert.That(Repack(), Is.EqualTo(new[] { Error((ItemRepackError) 2) }));
    }

    [Test]
    public void TargetNotYetExpired_Works() {
        Build();
        target.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

        Assert.That(Repack(), Is.EqualTo(new[] { ItemRepackPacket.Commit(target).ToArray() }));
    }

    [Test]
    public void ScrollNotInTheItemsList_Answers1() {
        Build(data => data with { Property = data.Property with { RepackScrollIds = [ScrollId + 1] } });

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target) }));
    }

    [Test]
    public void ScrollInTheItemsList_Works() {
        Build(data => data with { Property = data.Property with { RepackScrollIds = [ScrollId] } });

        Assert.That(Repack(), Is.EqualTo(new[] { ItemRepackPacket.Commit(target).ToArray() }));
    }

    [Test]
    public void SlotNotListed_Answers3() {
        Build(scrollData: new ItemRepackingScrollMetadata(30, 50, [14], [3], false));

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_impossible_slot) }));
    }

    [Test]
    public void PetScrollOnAnItem_Answers3() {
        Build(scrollData: new ItemRepackingScrollMetadata(30, 50, [13], [3], true));

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_impossible_slot) }));
    }

    [Test]
    public void RankNotListed_Answers4() {
        Build(rarity: 4);

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_impossible_rank) }));
    }

    [TestCase(29)]
    [TestCase(51)]
    public void LevelOutsideTheRange_Answers5(int level) {
        Build(data => data with { Limit = data.Limit with { Level = level } });

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_impossible_level) }));
    }

    [TestCase(30)]
    [TestCase(50)]
    public void LevelAtTheEdges_Works(int level) {
        Build(data => data with { Limit = data.Limit with { Level = level } });

        Assert.That(Repack(), Is.EqualTo(new[] { ItemRepackPacket.Commit(target).ToArray() }));
    }

    [TestCase(TransferType.Tradable)]
    [TestCase(TransferType.Untradeable)]
    [TestCase(TransferType.BindOnLoot)]
    [TestCase(TransferType.BindOnUse)]
    public void TransferTypeOtherThanEquipOrTrade_Answers8(TransferType type) {
        Build(data => data with { Limit = data.Limit with { TransferType = type } });

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_not_limit_tradable_item) }));
    }

    [Test]
    public void BindOnTrade_Works() {
        Build(data => data with { Limit = data.Limit with { TransferType = TransferType.BindOnTrade } });

        Assert.That(Repack(), Is.EqualTo(new[] { ItemRepackPacket.Commit(target).ToArray() }));
    }

    [Test]
    public void TradesLeft_Answers9() {
        Build();
        target.Transfer!.RemainTrades = 1;

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_tradable_item) }));
    }

    [Test]
    public void BoundItem_Answers1() {
        Build();
        target.Transfer!.Flag = TransferFlag.Bind;
        target.Transfer.RemainTrades = 0;
        target.Transfer.Bind(test.Session.Player.Value.Character);

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_invalid_target) }));
    }

    [Test]
    public void ItemWithoutRepackCount_Answers10() {
        Build(data => data with { Property = data.Property with { RepackCount = 0 } });

        Assert.That(Repack(), Is.EqualTo(new[] { Error(ItemRepackError.s_item_repacking_scroll_error_remain_count) }));
    }

    [Test]
    public void LockedItem_Answers11() {
        Build();
        target.IsLocked = true;

        Assert.That(Repack(), Is.EqualTo(new[] { Error((ItemRepackError) 11) }));
    }

    [Test]
    public void ItemInTheUnlockWait_Answers11() {
        Build();
        target.UnlockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

        Assert.That(Repack(), Is.EqualTo(new[] { Error((ItemRepackError) 11) }));
    }

    [Test]
    public void Refusals_LeaveTheScrollAndTheItem() {
        Build(rarity: 4);

        Repack();

        Assert.Multiple(() => {
            Assert.That(test.Session.Item.Inventory.Find(ScrollItem).Sum(i => i.Amount), Is.EqualTo(1));
            Assert.That(target.Transfer!.RepackageCount, Is.EqualTo(0));
            Assert.That(target.Transfer.RemainTrades, Is.EqualTo(0));
        });
    }

    [Test]
    public void UnknownCommand_IsIgnored() {
        Build();

        new ItemRepackHandler().Handle(test.Session, TestGameSession.Packet(w => {
            w.WriteByte(2);
            w.WriteLong(scroll.Uid);
            w.WriteLong(target.Uid);
        }));

        Assert.That(test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.ItemRepackage), Is.Empty);
    }
}

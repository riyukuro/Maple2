using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestItemInventory 4 (drop a count) and 5 (drop the stack): what the client refuses before it sends is not
// taken from a changed client either.
public class ItemDropHandlerTests {
    private TestGameSession test = null!;
    private readonly ItemInventoryHandler handler = new();
    private Item stack = null!;

    private GameSession Start() => Start(TestGameSession.Metadata(20000001, type: 2));

    private GameSession Start(ItemMetadata metadata) {
        test = new TestGameSession();
        stack = test.Store(metadata, amount: 5);
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private void Drop(GameSession session, int amount) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteLong(stack.Uid);
            writer.WriteInt(amount);
        }));
    }

    private void DropAll(GameSession session) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(stack.Uid);
        }));
    }

    [Test]
    public void LockedItem_StaysInTheBag() {
        GameSession session = Start();
        session.Item.Inventory.Get(stack.Uid)!.IsLocked = true;

        Drop(session, 2);
        DropAll(session);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Amount, Is.EqualTo(5));
    }

    // An item with a gemstone in a socket is not dropped or destroyed.
    [Test]
    public void ItemWithAGemstone_StaysInTheBag() {
        GameSession session = Start();
        Item item = session.Item.Inventory.Get(stack.Uid)!;
        item.Socket = new ItemSocket(1, 1);
        item.Socket.Sockets[0] = new ItemGemstone(40000001, stats: new ItemStats());

        Drop(session, 2);
        DropAll(session);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Amount, Is.EqualTo(5));
    }

    [Test]
    public void ItemInItsUnlockWait_StaysInTheBag() {
        GameSession session = Start();
        session.Item.Inventory.Get(stack.Uid)!.UnlockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

        DropAll(session);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Amount, Is.EqualTo(5));
    }

    // A count of 0 or below dropped the whole stack.
    [Test]
    public void CountBelowOne_DropsNothing() {
        GameSession session = Start();

        Drop(session, 0);
        Drop(session, -1);
        Drop(session, int.MinValue);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Amount, Is.EqualTo(5));
    }

    // More than the stack holds is refused by the bag; the stack is whole.
    [Test]
    public void CountOverTheStack_DropsNothing() {
        GameSession session = Start();

        Drop(session, 6);

        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Amount, Is.EqualTo(5));
    }

    private static ICollection<FieldItem> Ground(GameSession session) {
        var items = (ConcurrentDictionary<int, FieldItem>) typeof(FieldManager)
            .GetField("fieldItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Field)!;
        return items.Values;
    }

    // Mode 4 comes only for an item the client lets land on the field (CItemTransfer::CanDestroy): the count it
    // chose lands, the rest stays. The server's own Split flag is not asked.
    [TestCase(TransferFlag.Trade | TransferFlag.Split)]
    [TestCase(TransferFlag.Trade)]
    public void TradeableItem_DropsTheChosenCount(TransferFlag flag) {
        GameSession session = Start();
        session.Item.Inventory.Get(stack.Uid)!.Transfer = new ItemTransfer(flag);

        Drop(session, 2);

        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Amount, Is.EqualTo(3));
        Assert.That(Ground(session).Select(item => item.Value.Amount), Is.EqualTo(new[] { 2 }));
    }

    // The client sends mode 4 for a black-market-only item (its trade bit passes CanDestroy). At its trade rarity
    // with no trades left it may not go to another player, so it is destroyed and nothing lands; below that rarity
    // the chosen count lands.
    [TestCase(1, false)]
    [TestCase(2, true)]
    public void BlackMarketOnly_Drop(int tradeMaxRarity, bool lands) {
        ItemMetadata metadata = TestGameSession.Metadata(20000001, type: 2);
        GameSession session = Start(metadata with { Limit = metadata.Limit with { TransferType = TransferType.BlackMarketOnly, TradeMaxRarity = tradeMaxRarity } });
        Assert.That(session.Item.Inventory.Get(stack.Uid)!.Transfer!.Flag, Is.EqualTo(TransferFlag.Trade));

        Drop(session, 2);

        Assert.That(session.Item.Inventory.Get(stack.Uid)?.Amount, Is.EqualTo(lands ? 3 : null));
        Assert.That(Ground(session).Select(item => item.Value.Amount), Is.EqualTo(lands ? new[] { 2 } : Array.Empty<int>()));
    }

    // Mode 5 comes from the destroy box: the whole item goes and nothing lands, tradeable or not.
    [TestCase(TransferFlag.Trade)]
    [TestCase(TransferFlag.None)]
    public void DropAll_DestroysTheItem(TransferFlag flag) {
        GameSession session = Start();
        session.Item.Inventory.Get(stack.Uid)!.Transfer = new ItemTransfer(flag);

        DropAll(session);

        Assert.That(session.Item.Inventory.Get(stack.Uid), Is.Null);
        Assert.That(Ground(session), Is.Empty);
    }

    // A bound item or one with no trade bit never lands on the field.
    [Test]
    public void IsFieldDrop() {
        var item = new Item(TestGameSession.Metadata(20000001, type: 2));
        item.Transfer = new ItemTransfer(TransferFlag.Trade);
        Assert.That(ItemInventoryHandler.IsFieldDrop(item), Is.True);
        item.Transfer = new ItemTransfer(TransferFlag.LimitTrade, 1);
        Assert.That(ItemInventoryHandler.IsFieldDrop(item), Is.False);
        item.Transfer = new ItemTransfer(TransferFlag.Trade | TransferFlag.Bind, binding: new ItemBinding(1, "a"));
        Assert.That(ItemInventoryHandler.IsFieldDrop(item), Is.False);
        item.Transfer = null;
        Assert.That(ItemInventoryHandler.IsFieldDrop(item), Is.False);
    }

    [TestCase(false, true, 7, 0, 0, true)]
    [TestCase(true, true, 7, 0, 0, false)]   // dead
    [TestCase(false, false, 7, 0, 0, false)] // locked
    [TestCase(false, true, 7, 7, 0, false)]  // the pet that is out
    [TestCase(false, true, 7, 0, 7, false)]  // the score being played
    [TestCase(false, true, 7, 8, 9, true)]
    public void CanDrop(bool dead, bool usable, long uid, long pet, long score, bool expected) {
        Assert.That(ItemLockRules.CanDrop(dead, usable, uid, pet, score), Is.EqualTo(expected));
    }

    [TestCase(false, 0, 100, true)]
    [TestCase(true, 0, 100, false)]
    [TestCase(false, 101, 100, false)]
    [TestCase(false, 100, 100, true)]
    public void IsUsable(bool locked, long unlockTime, long now, bool expected) {
        Assert.That(ItemLockRules.IsUsable(locked, unlockTime, now), Is.EqualTo(expected));
    }
}

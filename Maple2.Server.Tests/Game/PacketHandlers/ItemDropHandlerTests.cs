using System;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestItemInventory 4 (drop a count) and 5 (drop the stack): what the client refuses before it sends is not
// taken from a changed client either.
public class ItemDropHandlerTests {
    private TestGameSession test = null!;
    private readonly ItemInventoryHandler handler = new();
    private Item stack = null!;

    private GameSession Start() {
        test = new TestGameSession();
        stack = test.Store(TestGameSession.Metadata(20000001, type: 2), amount: 5);
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

using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using GameItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The bag window's sort (RequestItemInventory 8: the tab and the answer to the client's "remove the expired items"
// box, UIInventoryDialog::OnSortConfirm): the expired items are removed only on that answer, a locked one is kept,
// stacks of one item are merged, and the tab is reloaded.
public class ItemInventorySortTests {
    private const int StackId = 40100001;
    private const int OtherId = 40100002;
    private TestGameSession test = null!;

    private GameSession Login() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(StackId, type: 4, slotMax: 10), TestGameSession.Metadata(OtherId, type: 4, slotMax: 10));
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private GameItem Add(GameSession session, int id, int amount) {
        GameItem item = session.Field!.ItemDrop.CreateItem(id, amount: amount)!;
        Assert.That(session.Item.Inventory.Add(item, commit: true), Is.True);
        return session.Item.Inventory.Find(id).OrderByDescending(found => found.Uid).First();
    }

    private void Sort(GameSession session, int tab, bool removeExpired) =>
        new ItemInventoryHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteByte((byte) tab);
            writer.WriteBool(removeExpired);
        }));

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // On the answer to remove them, the expired items go, except a locked one: the lock guards it.
    [Test]
    public void RemoveExpired_KeepsTheLockedAndTheRunning() {
        GameSession session = Login();
        GameItem expired = Add(session, StackId, 1);
        GameItem locked = Add(session, OtherId, 1);
        GameItem running = Add(session, OtherId, 10);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        expired.ExpiryTime = now - 60;
        locked.ExpiryTime = now - 60;
        locked.IsLocked = true;
        running.ExpiryTime = now + 3600;
        InventoryType type = expired.Inventory;
        test.TakeSent();

        Sort(session, type.ToClient(), removeExpired: true);

        Assert.That(session.Item.Inventory.Get(expired.Uid), Is.Null);
        Assert.That(session.Item.Inventory.Get(locked.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Get(running.Uid), Is.Not.Null);
        string[] sent = test.TakeSent().Select(Convert.ToHexString).ToArray();
        Assert.That(sent[0], Is.EqualTo(Hex(ItemInventoryPacket.Reset(type))));
        Assert.That(sent, Has.Length.EqualTo(2));
    }

    // With no expired item the client skips the box and sends false: an expired item is then kept.
    [Test]
    public void NoRemove_KeepsTheExpired() {
        GameSession session = Login();
        GameItem expired = Add(session, StackId, 1);
        expired.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
        test.TakeSent();

        Sort(session, expired.Inventory.ToClient(), removeExpired: false);

        Assert.That(session.Item.Inventory.Get(expired.Uid), Is.Not.Null);
    }

    // Two stacks of one item merge into the first: the emptied one is removed and the kept one updated before the
    // reload.
    [Test]
    public void MergesStacks_RemoveThenUpdateThenReload() {
        GameSession session = Login();
        GameItem first = Add(session, StackId, 10);
        GameItem second = Add(session, StackId, 3);
        first.Amount = 4;
        InventoryType type = first.Inventory;
        test.TakeSent();

        Sort(session, type.ToClient(), removeExpired: false);

        string[] sent = test.TakeSent().Select(Convert.ToHexString).ToArray();
        Assert.That(sent.Take(3), Is.EqualTo(new[] {
            Hex(ItemInventoryPacket.Remove(second.Uid)), Hex(ItemInventoryPacket.UpdateAmount(first.Uid, 7)), Hex(ItemInventoryPacket.Reset(type)),
        }));
        Assert.That(session.Item.Inventory.Find(StackId).Select(item => (item.Uid, item.Amount, item.Slot)), Is.EqualTo(new[] { (first.Uid, 7, (short) 0) }));
    }

    // A tab number past the client's 15 is not answered.
    [Test]
    public void UnknownTab_SendsNothing() {
        GameSession session = Login();
        Add(session, StackId, 1);
        test.TakeSent();

        Sort(session, InventoryTab.Count, removeExpired: true);

        Assert.That(test.TakeSent(), Is.Empty);
    }
}

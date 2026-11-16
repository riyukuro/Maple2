using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using GameItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The bag's drag and drop (InvenSlot::vfunc_18, invenslot.cpp:1145): the client sends RequestItemInventory 3 with the
// dragged item's uid and the slot it was dropped on, whatever is there; the server stacks it onto the same item or
// swaps the two.
public class ItemInventoryMoveTests {
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

    private void Move(GameSession session, long uid, short slot) =>
        new ItemInventoryHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(uid);
            writer.WriteShort(slot);
        }));

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // A stack dropped on the same item that has room for all of it goes into it: the dragged stack is removed.
    [Test]
    public void SameItem_AllFits_MergesAndRemovesTheDragged() {
        GameSession session = Login();
        GameItem full = Add(session, StackId, 10);
        GameItem rest = Add(session, StackId, 5);
        full.Amount = 2;
        test.TakeSent();

        Move(session, full.Uid, rest.Slot);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemInventoryPacket.Remove(full.Uid)), Hex(ItemInventoryPacket.UpdateAmount(rest.Uid, 7)),
        }));
        Assert.That(session.Item.Inventory.Find(StackId).Select(item => (item.Uid, item.Amount)), Is.EqualTo(new[] { (rest.Uid, 7) }));
    }

    // Room for part of it: the target fills to its slot max and the dragged stack keeps the rest, in its slot.
    [Test]
    public void SameItem_PartFits_FillsTheTarget() {
        GameSession session = Login();
        GameItem full = Add(session, StackId, 10);
        GameItem rest = Add(session, StackId, 5);
        short slot = full.Slot;
        test.TakeSent();

        Move(session, full.Uid, rest.Slot);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemInventoryPacket.UpdateAmount(full.Uid, 5)), Hex(ItemInventoryPacket.UpdateAmount(rest.Uid, 10)),
        }));
        Assert.That((full.Amount, full.Slot, rest.Amount), Is.EqualTo((5, slot, 10)));
    }

    // Another item (or a full stack) in the slot: the two swap slots in one Move answer.
    [Test]
    public void OtherItem_Swaps() {
        GameSession session = Login();
        GameItem a = Add(session, StackId, 3);
        GameItem b = Add(session, OtherId, 4);
        (short slotA, short slotB) = (a.Slot, b.Slot);
        test.TakeSent();

        Move(session, a.Uid, slotB);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(ItemInventoryPacket.Move(b.Uid, slotA, a.Uid, slotB)) }));
        Assert.That((a.Slot, b.Slot), Is.EqualTo((slotB, slotA)));
    }

    // A slot below 0 is answered with the invalid slot error; one past the tab is ignored; neither moves anything.
    [TestCase(-1, true)]
    [TestCase(short.MaxValue, false)]
    public void BadSlot_MovesNothing(int slot, bool error) {
        GameSession session = Login();
        GameItem a = Add(session, StackId, 3);
        short before = a.Slot;
        test.TakeSent();

        Move(session, a.Uid, (short) slot);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString),
            Is.EqualTo(error ? new[] { Hex(ItemInventoryPacket.Error(ItemInventoryError.s_item_err_Invalid_slot)) } : Array.Empty<string>()));
        Assert.That(a.Slot, Is.EqualTo(before));
    }
}

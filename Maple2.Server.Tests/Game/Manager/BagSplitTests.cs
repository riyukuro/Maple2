using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// An add of more than SlotMax goes into the bag in pieces, all of it or none (InventoryManager.Add). Stacks are
// matched by ItemCollection's stacking rule, so a locked stack of the same item is no room for it.
public class BagSplitTests {
    private const int Stackable = 20000002;
    private TestGameSession test = null!;

    // The bag tab of the item with `size` slots, holding a locked stack of `locked` (none for 0) in its last slot.
    internal static void Bag(GameSession session, StoredItem sample, short size, int locked) {
        var tabs = (Dictionary<InventoryType, ItemCollection>) typeof(InventoryManager)
            .GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Item.Inventory)!;
        ItemCollection old = tabs[sample.Inventory];
        var tab = new ItemCollection(size);
        foreach (StoredItem item in old) {
            Assert.That(tab.Add(item), Is.Not.Empty);
        }
        tabs[sample.Inventory] = tab;
        if (locked > 0) {
            StoredItem stack = session.Field!.ItemDrop.CreateItem(sample.Id)!;
            stack.Amount = locked;
            stack.IsLocked = true;
            Assert.That(session.Item.Inventory.Add(stack, commit: true), Is.True);
            // In the last slot, so the open slots come before it.
            Assert.That(tab.Remove(tab.Single(item => item.IsLocked).Uid, out StoredItem? moved), Is.True);
            moved!.Slot = (short) (size - 1);
            Assert.That(tab.Add(moved), Is.Not.Empty);
        }
    }

    internal static int Held(GameSession session, int itemId, bool locked = false) =>
        session.Item.Inventory.Find(itemId).Where(item => item.IsLocked == locked).Sum(item => item.Amount);

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Stackable));
        return test.Start();
    }

    private StoredItem Make(GameSession session, int amount) {
        StoredItem item = session.Field!.ItemDrop.CreateItem(Stackable)!;
        item.Amount = amount;
        return item;
    }

    private static int OpenSlots(GameSession session, StoredItem sample) {
        var tabs = (Dictionary<InventoryType, ItemCollection>) typeof(InventoryManager)
            .GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Item.Inventory)!;
        return tabs[sample.Inventory].OpenSlots;
    }

    // 250 at SlotMax 100 next to a locked stack of 50 needs three slots; with two open the bag takes none of it.
    [TestCase(2, false)]
    [TestCase(3, true)]
    public void Add_OfMoreThanSlotMax_TakesAllOrNone(int open, bool added) {
        GameSession session = Start();
        StoredItem add = Make(session, 250);
        Bag(session, add, (short) (session.Item.Inventory.Filter(_ => true, add.Inventory).Count + 1 + open), locked: 50);

        bool result = session.Item.Inventory.Add(add, commit: true);

        Assert.Multiple(() => {
            Assert.That(result, Is.EqualTo(added));
            Assert.That(Held(session, Stackable), Is.EqualTo(added ? 250 : 0));
            Assert.That(Held(session, Stackable, locked: true), Is.EqualTo(50));
            Assert.That(add.Amount, Is.EqualTo(250));
            Assert.That(OpenSlots(session, add), Is.EqualTo(added ? 0 : open));
        });
    }

    // A failed item write between the pieces: the pieces given stay, and the item keeps only the rest, so a caller
    // that mails or drops it on false hands out that rest and not the whole amount again.
    [Test]
    public void Add_FailedWriteMidway_LeavesTheRestOnTheItem() {
        GameSession session = Start();
        StoredItem add = Make(session, 250);
        int writes = 0;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item") && ++writes == 2;

        bool result = session.Item.Inventory.Add(add, commit: true);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(result, Is.False);
            Assert.That(Held(session, Stackable), Is.EqualTo(100));
            Assert.That(add.Amount, Is.EqualTo(150));
        });
    }

    // A drop of 250 is picked up only with room for all of it: refused, it stays on the ground with all 250 and the
    // bag is unchanged (a partial add put the full drop back, so its first part existed twice).
    [TestCase(2, false)]
    [TestCase(3, true)]
    public void Pickup_OfMoreThanSlotMax_NeedsRoomForAllOfIt(int open, bool taken) {
        GameSession session = Start();
        StoredItem add = Make(session, 250);
        Bag(session, add, (short) (session.Item.Inventory.Filter(_ => true, add.Inventory).Count + 1 + open), locked: 50);
        FieldItem drop = session.Field!.SpawnItem(session.Player.Position, session.Player.Rotation, add, null);
        test.TakeSent();

        new ItemPickupHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(drop.ObjectId);
            writer.WriteByte(0);
        }));

        var ground = (System.Collections.Concurrent.ConcurrentDictionary<int, FieldItem>) typeof(FieldManager)
            .GetField("fieldItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Field)!;
        int onGround = ground.Values.Where(item => item.Value.Id == Stackable).Sum(item => item.Value.Amount);
        Assert.Multiple(() => {
            Assert.That(Held(session, Stackable), Is.EqualTo(taken ? 250 : 0));
            Assert.That(onGround, Is.EqualTo(taken ? 0 : 250));
            Assert.That(Held(session, Stackable, locked: true), Is.EqualTo(50));
        });
    }
}

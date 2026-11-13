using System.Linq;
using System.Reflection;
using Maple2.Model.Game;
using StoredItem = Maple2.Model.Game.Item;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Manager;

// A furnishing item goes into furnishing storage, all of it or none (InventoryManager.Add), so the room checks
// before an add (CanAdd, HasRoomFor, TakesAll) ask storage the same thing.
public class FurnishingRoomTests {
    private const int CubeItem = 50400001;
    private const int OtherItem = 50400002;
    private const int MaidItem = 50400003;
    private TestGameSession test = null!;

    private GameSession Start(short storageSlots = Constant.FurnishingStorageMaxSlot) {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.Register(TestGameSession.Metadata(CubeItem, slotMax: 2));
        test.Register(TestGameSession.Metadata(OtherItem));
        test.Register(TestGameSession.Metadata(MaidItem, slotMax: 1) with { Housing = new ItemMetadataHousing(0, 0, 0, default, true, 3) });
        if (storageSlots != Constant.FurnishingStorageMaxSlot) {
            typeof(FurnishingManager).GetField("storage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(session.Item.Furnishing, new ItemCollection(storageSlots));
        }
        return session;
    }

    private StoredItem Make(GameSession session, int itemId, int amount) {
        StoredItem item = session.Field!.ItemDrop.CreateItem(itemId)!;
        item.Amount = amount;
        return item;
    }

    // A stack of 1 of an item with stack size 2: one more fits, two do not (no second stack of an item).
    [TestCase(1, true)]
    [TestCase(2, false)]
    public void RoomChecks_AskFurnishingStorage(int amount, bool fits) {
        GameSession session = Start();
        Assert.That(session.Item.Furnishing.AddCube(CubeItem), Is.Not.Zero);
        StoredItem item = Make(session, CubeItem, amount);

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.CanAdd(item), Is.EqualTo(fits));
            Assert.That(session.Item.Inventory.CanAdd(item, amount), Is.EqualTo(fits));
            Assert.That(session.Item.Inventory.HasRoomFor(item), Is.EqualTo(fits));
            Assert.That(session.Item.Inventory.TakesAll([item]), Is.EqualTo(fits));
            Assert.That(session.Item.Inventory.CanAdd(new[] { item }), Is.EqualTo(fits));
        });
    }

    // Two items that each fit but not together: storage has one open slot.
    [Test]
    public void RoomChecks_CountTheItemsTogether() {
        GameSession session = Start(storageSlots: 1);
        StoredItem first = Make(session, CubeItem, 1);
        StoredItem second = Make(session, OtherItem, 1);

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.HasRoomFor(first), Is.True);
            Assert.That(session.Item.Inventory.HasRoomFor(second), Is.True);
            Assert.That(session.Item.Inventory.TakesAll([first, second]), Is.False);
            Assert.That(session.Item.Inventory.CanAdd(new[] { first, second }), Is.False);
        });
    }

    // Each piece of a maid item takes a slot of its own.
    [TestCase(2, true)]
    [TestCase(3, false)]
    public void MaidItems_TakeASlotEach(int amount, bool fits) {
        GameSession session = Start(storageSlots: 2);

        Assert.That(session.Item.Inventory.HasRoomFor(Make(session, MaidItem, amount)), Is.EqualTo(fits));
    }

    // A failed item write in the middle of TakeMailed takes the pieces already stored out again: the caller keeps
    // the item (in the mail), so nothing is given twice.
    [Test]
    public void TakeMailed_TakesBackThePiecesStoredBeforeAFailedWrite() {
        GameSession session = Start();
        StoredItem maids = Make(session, MaidItem, 2);
        int writes = 0;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item") && ++writes == 2;

        bool taken = session.Item.Furnishing.TakeMailed(maids);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(taken, Is.False);
            Assert.That(writes, Is.EqualTo(2));
            Assert.That(session.Item.Furnishing.GetItem(MaidItem)?.Amount ?? 0, Is.Zero);
        });
    }

    // A field drop of a furnishing item whose storage stack is full is refused and stays on the ground; one with room
    // goes into storage.
    [TestCase(1, false)]
    [TestCase(0, true)]
    public void Pickup_OfAFurnishing_NeedsStorageRoom(int stored, bool taken) {
        GameSession session = Start();
        test.Register(TestGameSession.Metadata(OtherItem, slotMax: 1));
        if (stored > 0) {
            Assert.That(session.Item.Furnishing.AddCube(OtherItem), Is.Not.Zero);
        }
        FieldItem drop = session.Field!.SpawnItem(session.Player.Position, session.Player.Rotation, Make(session, OtherItem, 1), null);
        test.TakeSent();

        new ItemPickupHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(drop.ObjectId);
            writer.WriteByte(0);
        }));

        Assert.Multiple(() => {
            Assert.That(session.Field.TryGetItem(drop.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(session.Item.Furnishing.GetItem(OtherItem)?.Amount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TakeMailed_StoresEveryPiece() {
        GameSession session = Start();

        Assert.That(session.Item.Furnishing.TakeMailed(Make(session, CubeItem, 2)), Is.True);
        Assert.That(session.Item.Furnishing.GetItem(CubeItem)?.Amount, Is.EqualTo(2));
    }
}

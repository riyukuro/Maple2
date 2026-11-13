using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
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
    private const int Other = 20000003;
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
        test.Register(TestGameSession.Metadata(Stackable), TestGameSession.Metadata(Other));
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

    // Collecting a mailed 250 at SlotMax 100 moves the attachment's row, as read back from the database (a relog):
    // the mail keeps nothing and the bag has 250. With the second piece's write failing, the mail keeps 150 and the
    // bag has 100. The pieces were new rows next to the untouched attachment, so every relog gave all 250 again.
    [TestCase(false, 0, 250)]
    [TestCase(true, 150, 100)]
    public void MailCollect_OfMoreThanSlotMax_MovesTheAttachmentRow(bool failSecondPiece, int mailAfter, int bagAfter) {
        GameSession session = Start();
        long mailId = Mail(session, Make(session, 250));
        Assert.That(mailId, Is.Not.EqualTo(test.CharacterId));
        int writes = 0;
        if (failSecondPiece) {
            test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                                   && entry.Entity.GetType().Name == "Item") && ++writes == 2;
        }

        MailError collected = session.Mail.Collect(mailId);
        test.FailSave = null;

        using GameStorage.Request reload = test.Storage.Context();
        int mailed = reload.GetMail(mailId, test.CharacterId)!.Items.Where(item => item.Id == Stackable).Sum(item => item.Amount);
        int bag = reload.GetItemGroups(test.CharacterId, ItemGroup.Default)[ItemGroup.Default]
            .Where(item => item.Id == Stackable).Sum(item => item.Amount);
        Assert.Multiple(() => {
            Assert.That(collected, Is.EqualTo(failSecondPiece ? MailError.s_mail_error_receiveitem_to_inven : MailError.none));
            Assert.That(mailed, Is.EqualTo(mailAfter));
            Assert.That(bag, Is.EqualTo(bagAfter));
            Assert.That(Held(session, Stackable), Is.EqualTo(bagAfter));
        });
    }

    // The mail is collected only when the bag takes every attachment: 250 at SlotMax 100 needs three slots and 10 of
    // another item one, so three open slots take nothing (one slot per attachment passed, the 10 was given and the
    // collect stopped at the 250).
    [TestCase(3, false)]
    [TestCase(4, true)]
    public void MailCollect_NeedsTheSlotsOfEveryAttachment(int open, bool collected) {
        GameSession session = Start();
        StoredItem other = session.Field!.ItemDrop.CreateItem(Other)!;
        other.Amount = 10;
        StoredItem big = Make(session, 250);
        Bag(session, big, (short) (session.Item.Inventory.Filter(_ => true, big.Inventory).Count + open), locked: 0);
        long mailId = Mail(session, big, other);

        MailError error = session.Mail.Collect(mailId);

        Assert.Multiple(() => {
            Assert.That(error, Is.EqualTo(collected ? MailError.none : MailError.s_mail_error_receiveitem_to_inven));
            Assert.That(Held(session, Stackable), Is.EqualTo(collected ? 250 : 0));
            Assert.That(Held(session, Other), Is.EqualTo(collected ? 10 : 0));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A mail to the player with the items attached, in the player's inbox.
    private long Mail(GameSession session, params StoredItem[] items) {
        long mailId;
        using (GameStorage.Request db = test.Storage.Context()) {
            // Items are owned by a character, account or mail id; the database keeps their ranges apart
            // (AUTO_INCREMENT), the in-memory one starts each at 1, so one mail id is used up first.
            db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        }
        var mail = new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c" };
        foreach (StoredItem item in items) {
            using GameStorage.Request db = test.Storage.Context();
            mail.Items.Add(db.CreateItem(0, item)!);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            mailId = db.CreateMail(mail)!.Id;
        }
        session.Mail.Notify(force: true);
        return mailId;
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

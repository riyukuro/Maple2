using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// An item row the bag tab has no slot for at load (an ingest moved its item to another tab, or the tab shrank) goes
// to a system mail of its own, as a reward that finds the bag full; it was given owner 0 at the next save.
public class InventoryOverflowTests {
    [Test]
    public void AnItemWithoutASlot_IsMailedNotLost() {
        var test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [(short) 1, 0] });
        Item first = test.Store(TestGameSession.Metadata(30000001));
        Item second = test.Store(TestGameSession.Metadata(30000002));
        // Item rows are keyed by owner id alone (a character's or a mail's): mail ids past the character's id.
        using (GameStorage.Request other = test.Storage.Context()) {
            while (other.CreateMail(new Mail { Type = MailType.System, ReceiverId = -1 }) is { } made && made.Id <= test.CharacterId) { }
        }

        GameSession session = test.Start();
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Save(db);
        }

        Item[] held = session.Item.Inventory.Filter(_ => true).ToArray();
        Assert.That(held, Has.Length.EqualTo(1));
        long mailedUid = held[0].Uid == first.Uid ? second.Uid : first.Uid;
        using GameStorage.Request check = test.Storage.Context();
        Mail mail = check.GetAllMail(session.CharacterId).Single();
        Assert.Multiple(() => {
            Assert.That(mail.Type, Is.EqualTo(MailType.System));
            Assert.That(mail.Items.Select(item => item.Uid), Is.EqualTo(new[] { mailedUid }));
            Assert.That(check.GetInventory(session.CharacterId).Values.SelectMany(items => items).Select(item => item.Uid),
                Is.EqualTo(new[] { held[0].Uid }));
        });
    }

    // Every item fits: no mail.
    [Test]
    public void ItemsThatFit_AreNotMailed() {
        var test = new TestGameSession();
        test.Store(TestGameSession.Metadata(30000001));
        test.Store(TestGameSession.Metadata(30000002));

        GameSession session = test.Start();

        using GameStorage.Request check = test.Storage.Context();
        Assert.That(check.GetAllMail(session.CharacterId), Is.Empty);
        Assert.That(session.Item.Inventory.Filter(_ => true).Count(), Is.EqualTo(2));
    }
}

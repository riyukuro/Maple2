using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// Each attachment of a mail is given once (MailManager.Collect re-reads the mail from the database).
public class MailCollectTests {
    private const int MesoItem = 90000001;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(MesoItem));
        return test.Start();
    }

    // A meso item on a mail: collected, its row leaves the mail at once, so collecting the mail again (a second
    // mode 11, or the mail list read back from the database) gives nothing more. The row went on the bag's delete
    // list only, still the mail's until the next character save, and every collect gave the mesos again.
    [Test]
    public void Collect_CurrencyItem_IsGivenOnce() {
        GameSession session = Start();
        StoredItem meso = session.Field!.ItemDrop.CreateItem(MesoItem)!;
        meso.Amount = 1000;
        long before = session.Currency.Meso;
        long mailId = Mail(session, meso);

        MailError first = session.Mail.Collect(mailId);
        MailError second = session.Mail.Collect(mailId);

        using GameStorage.Request reload = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(first, Is.EqualTo(MailError.none));
            Assert.That(second, Is.EqualTo(MailError.s_mail_error_already_receive));
            Assert.That(session.Currency.Meso - before, Is.EqualTo(1000));
            Assert.That(reload.GetMail(mailId, test.CharacterId)!.Items, Is.Empty);
        });
    }

    // The meso item's row is written to owner 0 before the mesos are given: a write that fails gives nothing and
    // leaves the item on the mail, so the next collect gives it once. The mesos were given first and the row stayed
    // the mail's until the next character save, so a collect in between gave them again.
    [Test]
    public void Collect_CurrencyItem_FailedWrite_GivesNothing() {
        GameSession session = Start();
        StoredItem meso = session.Field!.ItemDrop.CreateItem(MesoItem)!;
        meso.Amount = 1000;
        long before = session.Currency.Meso;
        long mailId = Mail(session, meso);

        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                                                                               && entry.Entity.GetType().Name == "Item");
        MailError failed = session.Mail.Collect(mailId);
        test.FailSave = null;
        long afterFailed = session.Currency.Meso;
        int left;
        using (GameStorage.Request db = test.Storage.Context()) {
            left = db.GetMail(mailId, test.CharacterId)!.Items.Count;
        }

        MailError first = session.Mail.Collect(mailId);
        MailError second = session.Mail.Collect(mailId);

        Assert.Multiple(() => {
            Assert.That(failed, Is.EqualTo(MailError.s_mail_error_receiveitem_to_inven));
            Assert.That(afterFailed, Is.EqualTo(before));
            Assert.That(left, Is.EqualTo(1));
            Assert.That(first, Is.EqualTo(MailError.none));
            Assert.That(second, Is.EqualTo(MailError.s_mail_error_already_receive));
            Assert.That(session.Currency.Meso - before, Is.EqualTo(1000));
        });
    }

    // A mail that ran out is deleted with its attachment when the inbox is fetched (the login's count) and when the
    // list is opened (mode 0); the list leaves it out. It stayed in the list for good: the client hides it but counts
    // it in the pages, and its attachment could not be collected any more.
    [TestCase(false)]
    [TestCase(true)]
    public void ExpiredMail_IsDeletedWithItsAttachment(bool atLoad) {
        GameSession session = Start();
        StoredItem meso = session.Field!.ItemDrop.CreateItem(MesoItem)!;
        long live = Mail(session, 0);
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (atLoad ? 2 : -60);
        long expired = Mail(session, expiry, meso);
        if (atLoad) {
            using (GameStorage.Request db = test.Storage.Context()) {
                Assert.That(db.GetMail(expired, test.CharacterId), Is.Not.Null);
            }
            System.Threading.Thread.Sleep(2500);
        }
        test.TakeSent();

        session.Mail.Load();

        using GameStorage.Request reload = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(reload.GetMail(expired, test.CharacterId), Is.Null);
            Assert.That(reload.GetMail(live, test.CharacterId), Is.Not.Null);
            Assert.That(reload.GetAllItems(expired), Is.Empty);
            Assert.That(session.Mail.Collect(expired), Is.EqualTo(MailError.mail_not_found));
        });
    }

    private long Mail(GameSession session, params StoredItem[] items) => Mail(session, 0, items);

    // A mail to the player with the items attached, in the player's inbox; expiry 0 is the default.
    private long Mail(GameSession session, long expiry, params StoredItem[] items) {
        long mailId;
        using (GameStorage.Request db = test.Storage.Context()) {
            // Items are owned by a character, account or mail id; the in-memory database starts each range at 1, so
            // one mail id is used up first (as BagSplitTests).
            db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        }
        var mail = new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c", ExpiryTime = expiry };
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
}

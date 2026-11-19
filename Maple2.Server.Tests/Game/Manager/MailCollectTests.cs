using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
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

    // Mesos that would take the wallet over MaxMeso stay on the mail: the collect is refused with the mail error box,
    // gives nothing, does not send mode 11 (it claims the mesos too) and leaves the mesos uncollected in the database,
    // so they come once the wallet has room. They were collected anyway and the part over the cap was lost.
    [Test]
    public void Collect_MesoOverTheCap_StaysOnTheMail() {
        GameSession session = Start();
        session.Currency.Meso = Constant.MaxMeso - 40;
        long mailId = MesoMail(session, 100);
        test.TakeSent();

        MailError refused = session.Mail.Collect(mailId);
        long walletAfterRefused = session.Currency.Meso;
        byte[] mailModes = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail).Select(packet => packet[2]).ToArray();
        long collectTime;
        using (GameStorage.Request db = test.Storage.Context()) {
            collectTime = db.GetMail(mailId, test.CharacterId)!.MesoCollectTime;
        }

        session.Currency.Meso = 0;
        MailError collected = session.Mail.Collect(mailId);

        Assert.Multiple(() => {
            Assert.That(refused, Is.EqualTo(MailError.s_mail_error));
            Assert.That(walletAfterRefused, Is.EqualTo(Constant.MaxMeso - 40));
            Assert.That(mailModes, Does.Not.Contain((byte) 10).And.Not.Contain((byte) 11));
            Assert.That(collectTime, Is.EqualTo(0));
            Assert.That(collected, Is.EqualTo(MailError.none));
            Assert.That(session.Currency.Meso, Is.EqualTo(100));
        });
    }

    // The merets of a mail are given only by the collect that sends mode 11 (MailManager.CollectCurrency): the client
    // marks them collected with mode 11 alone (OnMail, scenestategame.cpp:11563), and mode 10 has a meso and a slot
    // form only (scenestategame.cpp:11547). Mesos over the wallet cap stop the collect, so the merets stay on the mail
    // with them and come with the collect that takes the mesos. They were given while the mesos stayed, with no
    // packet, and the client listed them on the mail until the list was read again.
    [Test]
    public void Collect_MesoOverTheCap_KeepsTheMeretsOnTheMail() {
        GameSession session = Start();
        session.Currency.Meso = Constant.MaxMeso - 40;
        long meretsBefore = session.Currency.Meret;
        long mailId = CurrencyMail(session, 100, 7);
        test.TakeSent();

        MailError refused = session.Mail.Collect(mailId);
        long meretsAfterRefused = session.Currency.Meret;
        byte[] refusedModes = MailModes();
        long meretTime = Stored(mailId).MeretCollectTime;

        session.Currency.Meso = 0;
        MailError collected = session.Mail.Collect(mailId);

        Assert.Multiple(() => {
            Assert.That(refused, Is.EqualTo(MailError.s_mail_error));
            Assert.That(meretsAfterRefused, Is.EqualTo(meretsBefore));
            Assert.That(refusedModes, Does.Not.Contain((byte) 10).And.Not.Contain((byte) 11));
            Assert.That(meretTime, Is.EqualTo(0));
            Assert.That(collected, Is.EqualTo(MailError.none));
            Assert.That(MailModes(), Is.EqualTo(new byte[] { 10, 11 }));
            Assert.That(session.Currency.Meso, Is.EqualTo(100));
            Assert.That(session.Currency.Meret - meretsBefore, Is.EqualTo(7));
            Assert.That(Stored(mailId).MeretCollectTime, Is.GreaterThan(0));
        });
    }

    // An item that cannot be added stops the collect before any currency is given: the items are given first and
    // the mesos and merets only after them, so the stopped collect leaves the mail's mesos and merets on it (neither
    // reported nor given) and the next collect gives them once, closing with mode 11. The currency was given first,
    // and the merets of the stopped collect were never reported.
    [Test]
    public void Collect_FailedItemAdd_KeepsTheCurrencyOnTheMail() {
        GameSession session = Start();
        StoredItem meso = session.Field!.ItemDrop.CreateItem(MesoItem)!;
        meso.Amount = 1000;
        long mesoBefore = session.Currency.Meso;
        long meretsBefore = session.Currency.Meret;
        long mailId = CurrencyMail(session, 300, 7, meso);
        test.TakeSent();

        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                                                                               && entry.Entity.GetType().Name == "Item");
        MailError failed = session.Mail.Collect(mailId);
        test.FailSave = null;
        long mesoAfterFailed = session.Currency.Meso;
        long meretsAfterFailed = session.Currency.Meret;
        byte[] failedModes = MailModes();
        Mail left = Stored(mailId);

        MailError collected = session.Mail.Collect(mailId);

        Assert.Multiple(() => {
            Assert.That(failed, Is.EqualTo(MailError.s_mail_error_receiveitem_to_inven));
            Assert.That(mesoAfterFailed, Is.EqualTo(mesoBefore));
            Assert.That(meretsAfterFailed, Is.EqualTo(meretsBefore));
            Assert.That(failedModes, Does.Not.Contain((byte) 10).And.Not.Contain((byte) 11));
            Assert.That(left.Items, Has.Count.EqualTo(1));
            Assert.That(left.MesoCollectTime, Is.EqualTo(0));
            Assert.That(left.MeretCollectTime, Is.EqualTo(0));
            Assert.That(collected, Is.EqualTo(MailError.none));
            Assert.That(MailModes(), Is.EqualTo(new byte[] { 10, 10, 11 }), "the item slot, the mesos, then the close");
            Assert.That(session.Currency.Meso - mesoBefore, Is.EqualTo(1300));
            Assert.That(session.Currency.Meret - meretsBefore, Is.EqualTo(7));
        });
    }

    // Merets over their cap stay on the mail, and mode 11, which would mark them collected, is not sent: the mesos
    // are given and reported with mode 10, and the collect is refused with the mail error box as for a full wallet.
    // Mode 11 was sent, and the client showed merets as collected that were still on the mail. Once the merets fit,
    // the next collect gives them alone and closes the mail with mode 11; a third is answered already collected.
    [Test]
    public void Collect_MeretsOverTheCap_StayAndAreNotShownCollected() {
        GameSession session = Start();
        session.Currency.Meret = Constant.MaxMeret - 2;
        long mailId = CurrencyMail(session, 100, 7);

        CollectOverTheCap(session, mailId, () => session.Currency.Meret, value => session.Currency.Meret = value,
            mail => mail.MeretCollectTime);
    }

    // Game merets over their cap: the same as the merets (mode 11 alone marks them collected).
    [Test]
    public void Collect_GameMeretsOverTheCap_StayAndAreNotShownCollected() {
        GameSession session = Start();
        session.Currency.GameMeret = Constant.MaxMeret - 2;
        long mailId = Mail(session, new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c", Meso = 100, GameMeret = 7 });

        CollectOverTheCap(session, mailId, () => session.Currency.GameMeret, value => session.Currency.GameMeret = value,
            mail => mail.GameMeretCollectTime);
    }

    // A meso item whose mesos would take the wallet over MaxMeso stays on the mail: the collect is refused before
    // anything is given (CollectCheck), as for the mail's own mesos. The item was added and the wallet cap
    // (CurrencyManager.ChangeMeso) dropped the part over it.
    [Test]
    public void Collect_MesoItemOverTheCap_StaysOnTheMail() {
        GameSession session = Start();
        StoredItem meso = session.Field!.ItemDrop.CreateItem(MesoItem)!;
        meso.Amount = 1000;
        session.Currency.Meso = Constant.MaxMeso - 40;
        long mailId = Mail(session, meso);
        test.TakeSent();

        MailError refused = session.Mail.Collect(mailId);
        long walletAfterRefused = session.Currency.Meso;
        byte[] refusedModes = MailModes();
        int itemsLeft = Stored(mailId).Items.Count;

        session.Currency.Meso = 0;
        MailError collected = session.Mail.Collect(mailId);

        Assert.Multiple(() => {
            Assert.That(refused, Is.EqualTo(MailError.s_mail_error));
            Assert.That(walletAfterRefused, Is.EqualTo(Constant.MaxMeso - 40));
            Assert.That(refusedModes, Does.Not.Contain((byte) 10).And.Not.Contain((byte) 11));
            Assert.That(itemsLeft, Is.EqualTo(1));
            Assert.That(collected, Is.EqualTo(MailError.none));
            Assert.That(session.Currency.Meso, Is.EqualTo(1000));
            Assert.That(Stored(mailId).Items, Is.Empty);
        });
    }

    // The mail holds 100 mesos and 7 of a currency whose balance is 2 under its cap: the first collect gives the mesos
    // only, the second (balance set to 0) the 7 alone with mode 11, the third nothing.
    private void CollectOverTheCap(GameSession session, long mailId, Func<long> balance, Action<long> setBalance, Func<Mail, long> collectTime) {
        long mesoBefore = session.Currency.Meso;
        test.TakeSent();

        MailError refused = session.Mail.Collect(mailId);
        byte[] refusedModes = MailModes();
        long mesoAfterRefused = session.Currency.Meso;
        long balanceAfterRefused = balance();
        Mail left = Stored(mailId);

        setBalance(0);
        MailError collected = session.Mail.Collect(mailId);
        byte[] collectedModes = MailModes();
        long mesoAfterCollected = session.Currency.Meso;
        long balanceAfterCollected = balance();
        long timeAfterCollected = collectTime(Stored(mailId));

        MailError again = session.Mail.Collect(mailId);

        Assert.Multiple(() => {
            Assert.That(refused, Is.EqualTo(MailError.s_mail_error));
            Assert.That(refusedModes, Is.EqualTo(new byte[] { 2, 10 }), "read (the collect reads an unread mail), then the mesos");
            Assert.That(mesoAfterRefused - mesoBefore, Is.EqualTo(100));
            Assert.That(balanceAfterRefused, Is.EqualTo(Constant.MaxMeret - 2));
            Assert.That(left.MesoCollectTime, Is.GreaterThan(0));
            Assert.That(collectTime(left), Is.EqualTo(0));

            Assert.That(collected, Is.EqualTo(MailError.none));
            Assert.That(collectedModes, Is.EqualTo(new byte[] { 11 }));
            Assert.That(mesoAfterCollected, Is.EqualTo(mesoAfterRefused), "the mesos are not given twice");
            Assert.That(balanceAfterCollected, Is.EqualTo(7));
            Assert.That(timeAfterCollected, Is.GreaterThan(0));

            Assert.That(again, Is.EqualTo(MailError.s_mail_error_already_receive));
            Assert.That(session.Currency.Meso, Is.EqualTo(mesoAfterCollected));
            Assert.That(balance(), Is.EqualTo(7));
        });
    }

    // The modes of the Mail packets sent since the last take.
    private byte[] MailModes() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail).Select(packet => packet[2]).ToArray();

    private Mail Stored(long mailId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMail(mailId, test.CharacterId)!;
    }

    // A mail with mesos and no attachment, in the player's inbox.
    private long MesoMail(GameSession session, long meso) {
        long mailId;
        using (GameStorage.Request db = test.Storage.Context()) {
            mailId = db.CreateMail(new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c", Meso = meso })!.Id;
        }
        session.Mail.Notify(force: true);
        return mailId;
    }

    private long Mail(GameSession session, params StoredItem[] items) => Mail(session, 0, items);

    private long Mail(GameSession session, long expiry, params StoredItem[] items) =>
        Mail(session, new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c", ExpiryTime = expiry }, items);

    // A mail to the player with mesos, merets and the items attached, in the player's inbox.
    private long CurrencyMail(GameSession session, long meso, long meret, params StoredItem[] items) =>
        Mail(session, new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c", Meso = meso, Meret = meret }, items);

    // The mail with the items attached, in the player's inbox; expiry 0 is the default.
    private long Mail(GameSession session, Mail mail, params StoredItem[] items) {
        long mailId;
        using (GameStorage.Request db = test.Storage.Context()) {
            // Items are owned by a character, account or mail id; the in-memory database starts each range at 1, so
            // one mail id is used up first (as BagSplitTests).
            db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        }
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

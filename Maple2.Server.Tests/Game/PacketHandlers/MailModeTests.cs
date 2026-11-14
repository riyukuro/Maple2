using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The Mail requests the client sends that the other mail tests leave out: delete all (13 with a count), collect all
// (19), collect of merets, a collect after the mail ran out, a collect from a second inbox of the same mail (the
// inbox a channel loaded before another collected it), and the ad bill (12).
public class MailModeTests {
    private const int ItemId = 20000005;

    private TestGameSession test = null!;
    private readonly MailHandler handler = new();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private void Begin() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId));
        // The test storage numbers mails from 1, so a mail could share the character's id and its items would load
        // into the bag (the live ids keep mails apart from characters): mails to nobody take the low ids.
        using GameStorage.Request db = test.Storage.Context();
        while (db.CreateMail(new Mail { ReceiverId = -1, Type = MailType.System, Title = "t", Content = "c" })!.Id <= test.CharacterId) { }
    }

    private GameSession Login() {
        GameSession session = test.Start();
        session.Currency.Meso = 1000;
        test.TakeSent();
        return session;
    }

    // A system mail to the player, with mesos, merets and an item of `amount` (none for 0), in the player's inbox.
    private Mail StoreMail(long meso = 0, long meret = 0, int amount = 0, bool locked = false) {
        using GameStorage.Request db = test.Storage.Context();
        Mail mail = db.CreateMail(new Mail {
            ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c", Meso = meso, Meret = meret,
        }) ?? throw new InvalidOperationException("mail row not created");
        if (amount > 0) {
            ItemMetadata metadata = TestGameSession.Metadata(ItemId);
            mail.Items.Add(db.CreateItem(mail.Id, new Item(metadata, amount: amount) { IsLocked = locked })
                           ?? throw new InvalidOperationException("item row not created"));
        }
        return mail;
    }

    private void Request(GameSession session, byte command, params long[] mailIds) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            if (command is 13 or 18 or 19) {
                writer.WriteInt(mailIds.Length);
            }
            foreach (long mailId in mailIds) {
                writer.WriteLong(mailId);
            }
        }));
    }

    private Mail? Stored(long mailId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMail(mailId, test.CharacterId);
    }

    private int Held(GameSession session) => session.Item.Inventory.Find(ItemId).Sum(item => item.Amount);

    private List<byte[]> MailSent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail).ToList();

    // Delete all: each mail of the list goes, with its uncollected item; a mail with a locked item stays (the client
    // asks for it only when HasLockedAttachment allows) and the rest of the list is still deleted.
    [Test]
    public void DeleteAll_DeletesEachMailButOneWithALockedItem() {
        Begin();
        Mail first = StoreMail(meso: 100);
        Mail locked = StoreMail(amount: 1, locked: true);
        Mail last = StoreMail(amount: 2);
        GameSession session = Login();
        session.Mail.Notify(force: true);
        test.TakeSent();

        Request(session, 13, first.Id, locked.Id, last.Id);

        Assert.Multiple(() => {
            Assert.That(MailSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(MailPacket.Deleted(first.Id)), Hex(MailPacket.Deleted(last.Id)) }));
            Assert.That(Stored(first.Id), Is.Null);
            Assert.That(Stored(last.Id), Is.Null);
            Assert.That(Stored(locked.Id)?.Items, Has.Count.EqualTo(1));
            Assert.That(session.Currency.Meso, Is.EqualTo(1000));
            Assert.That(Held(session), Is.EqualTo(0));
        });
    }

    // Collect all: the mesos, merets and items of each mail are given once; a mail already collected is passed over
    // and the rest of the list is still collected.
    [Test]
    public void CollectAll_GivesEachMailOnce() {
        Begin();
        Mail done = StoreMail(meso: 100);
        Mail items = StoreMail(meso: 200, amount: 3);
        Mail merets = StoreMail(meret: 5);
        GameSession session = Login();
        long meretsBefore = session.Currency.Meret;
        Request(session, 11, done.Id);
        test.TakeSent();

        Request(session, 19, done.Id, items.Id, merets.Id);
        Request(session, 19, done.Id, items.Id, merets.Id);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(1300));
            Assert.That(session.Currency.Meret - meretsBefore, Is.EqualTo(5));
            Assert.That(Held(session), Is.EqualTo(3));
            Assert.That(Stored(items.Id)!.Items, Is.Empty);
            Assert.That(Stored(items.Id)!.MesoCollectTime, Is.GreaterThan(0));
            Assert.That(Stored(merets.Id)!.MeretCollectTime, Is.GreaterThan(0));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The same mail in a second inbox loaded before the first collect (the inbox of a channel the player was in): the
    // collect reads the mail again from storage, so the second inbox gives nothing and answers already collected.
    [Test]
    public void Collect_FromAnInboxLoadedBeforeTheCollect_GivesNothing() {
        Begin();
        Mail mail = StoreMail(meso: 300, meret: 7, amount: 2);
        GameSession session = Login();
        long meretsBefore = session.Currency.Meret;
        var other = new MailManager(session);

        MailError first = session.Mail.Collect(mail.Id);
        MailError second = other.Collect(mail.Id);

        Assert.Multiple(() => {
            Assert.That(first, Is.EqualTo(MailError.none));
            Assert.That(second, Is.EqualTo(MailError.s_mail_error_already_receive));
            Assert.That(session.Currency.Meso, Is.EqualTo(1300));
            Assert.That(session.Currency.Meret - meretsBefore, Is.EqualTo(7));
            Assert.That(Held(session), Is.EqualTo(2));
        });
    }

    // Two collects of one mail that both read it before either wrote it (two game servers holding the player; login
    // keeps a character in one session, this is the insurance): the write of the collect is conditional, so the one
    // that writes second gives nothing. Before, it gave the mesos and merets again.
    [Test]
    public void Collect_OfAMailReadBeforeAnotherCollectWroteIt_GivesNothing() {
        Begin();
        Mail mail = StoreMail(meso: 300, meret: 7);
        GameSession session = Login();
        long meretsBefore = session.Currency.Meret;
        Mail stale = Stored(mail.Id)!;

        MailError first = session.Mail.Collect(mail.Id);
        using GameStorage.Request db = test.Storage.Context();
        var collect = typeof(MailManager).GetMethod("CollectInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var second = (MailError) collect.Invoke(session.Mail, [db, stale])!;

        Assert.Multiple(() => {
            Assert.That(first, Is.EqualTo(MailError.none));
            Assert.That(second, Is.Not.EqualTo(MailError.none));
            Assert.That(session.Currency.Meso, Is.EqualTo(1300));
            Assert.That(session.Currency.Meret - meretsBefore, Is.EqualTo(7));
        });
    }

    // The same through storage: of two collect writes from the same read, only the first is kept.
    [Test]
    public void CollectMail_OfTwoWritersFromOneRead_KeepsTheFirst() {
        Begin();
        Mail mail = StoreMail(meso: 300, meret: 7);
        using GameStorage.Request one = test.Storage.Context();
        using GameStorage.Request two = test.Storage.Context();
        Mail a = one.GetMail(mail.Id, test.CharacterId)!;
        Mail b = two.GetMail(mail.Id, test.CharacterId)!;
        a.MesoCollectTime = a.MeretCollectTime = 100;
        b.MesoCollectTime = b.MeretCollectTime = 200;

        bool first = one.CollectMail(a, (0, 0, 0));
        bool second = two.CollectMail(b, (0, 0, 0));

        Assert.Multiple(() => {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(Stored(mail.Id)!.MesoCollectTime, Is.EqualTo(100));
            Assert.That(Stored(mail.Id)!.MeretCollectTime, Is.EqualTo(100));
        });
    }

    // A mail that ran out after the inbox was fetched: the collect reads its expiry from storage and is refused with
    // code 21 (s_mail_error_receive_expired, command 11); nothing is given and the attachments stay.
    [Test]
    public void Collect_AfterTheMailRanOut_IsRefused() {
        Begin();
        Mail mail = StoreMail(meso: 300, amount: 2);
        GameSession session = Login();
        session.Mail.Notify(force: true);
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            Maple2.Database.Model.Mail row = context.Mail.Single(stored => stored.Id == mail.Id);
            row.ExpiryTime = DateTime.Now.AddDays(-1);
            context.SaveChanges();
        }
        test.TakeSent();

        Request(session, 11, mail.Id);

        Assert.Multiple(() => {
            Assert.That(MailSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(MailPacket.Error(MailError.s_mail_error_receive_expired, 11)) }));
            Assert.That(session.Currency.Meso, Is.EqualTo(1000));
            Assert.That(Held(session), Is.EqualTo(0));
            Assert.That(Stored(mail.Id)!.Items, Has.Count.EqualTo(1));
            Assert.That(Stored(mail.Id)!.MesoCollectTime, Is.EqualTo(0));
        });
    }

    // Mail 12 pays for an ad mail (kind 200), which the server never makes: the request changes nothing.
    [Test]
    public void AdBill_ChangesNothing() {
        Begin();
        Mail mail = StoreMail(meso: 300, amount: 2);
        GameSession session = Login();
        test.TakeSent();

        Request(session, 12, mail.Id);

        Assert.Multiple(() => {
            Assert.That(MailSent(), Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(1000));
            Assert.That(Stored(mail.Id)!.Items, Has.Count.EqualTo(1));
            Assert.That(Stored(mail.Id)!.ReadTime, Is.EqualTo(0));
        });
    }
}

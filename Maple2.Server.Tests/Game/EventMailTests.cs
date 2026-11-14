using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The CharacterFirstConnect and SendMail game events: system mails that carry a string/systemmailcontent id.
public class EventMailTests {
    private const long CharacterId = 1001;
    private const long Day = 86400;
    private const long Now = 1794484800; // 2026-11-12 12:00 UTC

    private static GameEventMetadata Row(int id, GameEventType type, GameEventData data) =>
        new(id, type, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");

    // KR 110000: value1 160331001.
    [Test]
    public void FirstConnectReader_KrRow() {
        Assert.That(EventMailReader.FirstConnect("160331001"), Is.EqualTo(new CharacterFirstConnectMail(160331001)));
        Assert.That(EventMailReader.FirstConnect(" 160331001 "), Is.EqualTo(new CharacterFirstConnectMail(160331001)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("x")]
    public void FirstConnectReader_Unusable(string? value1) {
        Assert.That(EventMailReader.FirstConnect(value1), Is.Null);
    }

    // KR 10100100: ReturnUser, "170531002,3", 90.
    [Test]
    public void SendMailReader_KrRow() {
        Assert.That(EventMailReader.SendMail("ReturnUser", "170531002,3", "90"),
            Is.EqualTo(new SendMailEvent("ReturnUser", 170531002, MailType.Notice, 90)));
    }

    // No kind: a system mail.
    [Test]
    public void SendMailReader_NoKind() {
        Assert.That(EventMailReader.SendMail("ReturnUser", "170531002", "90"),
            Is.EqualTo(new SendMailEvent("ReturnUser", 170531002, MailType.System, 90)));
    }

    // No mail, no target, a kind whose content the client does not read as a table id, or a ReturnUser row
    // without days is not stored.
    [TestCase(null, "170531002,3", "90")]
    [TestCase("", "170531002,3", "90")]
    [TestCase("ReturnUser", null, "90")]
    [TestCase("ReturnUser", "x,3", "90")]
    [TestCase("ReturnUser", "0,3", "90")]
    [TestCase("ReturnUser", "170531002,1", "90")]
    [TestCase("ReturnUser", "170531002,x", "90")]
    [TestCase("ReturnUser", "170531002,3", "")]
    [TestCase("ReturnUser", "170531002,3", "0")]
    public void SendMailReader_Unusable(string? value1, string? value2, string? value3) {
        Assert.That(EventMailReader.SendMail(value1, value2, value3), Is.Null);
    }

    // The client has no class for either type: the event list leaves both out.
    [Test]
    public void NotSentToClient() {
        Assert.Multiple(() => {
            Assert.That(new GameEvent(Row(110000, GameEventType.CharacterFirstConnect, new CharacterFirstConnectMail(160331001))).SentToClient,
                Is.False);
            Assert.That(new GameEvent(Row(10100100, GameEventType.SendMail, new SendMailEvent("ReturnUser", 170531002, MailType.Notice, 90)))
                .SentToClient, Is.False);
        });
    }

    // One system mail per CharacterFirstConnect row, its content the table id; other types mail nothing.
    [Test]
    public void FirstConnect_MailsTheContentId() {
        GameEventMetadata[] running = [
            Row(110000, GameEventType.CharacterFirstConnect, new CharacterFirstConnectMail(160331001)),
            Row(10100100, GameEventType.SendMail, new SendMailEvent("ReturnUser", 170531002, MailType.Notice, 90)),
        ];
        Mail[] mails = EventMailRules.FirstConnect(running, CharacterId).ToArray();
        Assert.That(mails.Select(mail => (mail.Type, mail.ReceiverId, mail.Content)),
            Is.EqualTo(new[] { (MailType.System, CharacterId, "160331001") }));
    }

    // 90 days away mails the notice; 89 days and a bit, another target or a character never saved does not.
    [Test]
    public void ReturnUser_AfterTheDays() {
        GameEventMetadata[] running = [
            Row(10100100, GameEventType.SendMail, new SendMailEvent("ReturnUser", 170531002, MailType.Notice, 90)),
            Row(10100101, GameEventType.SendMail, new SendMailEvent("NewUser", 170531003, MailType.Notice, 0)),
            Row(110000, GameEventType.CharacterFirstConnect, new CharacterFirstConnectMail(160331001)),
        ];
        Assert.Multiple(() => {
            Assert.That(EventMailRules.ReturnUser(running, CharacterId, Now - 90 * Day, Now).Select(mail => (mail.Type, mail.ReceiverId, mail.Content)),
                Is.EqualTo(new[] { (MailType.Notice, CharacterId, "170531002") }));
            Assert.That(EventMailRules.ReturnUser(running, CharacterId, Now - 90 * Day + 1, Now), Is.Empty);
            Assert.That(EventMailRules.ReturnUser(running, CharacterId, Now - 89 * Day, Now), Is.Empty);
            Assert.That(EventMailRules.ReturnUser(running, CharacterId, 0, Now), Is.Empty);
        });
    }

    // Through LoadPlayer: the rule gets the character's last save (its row version before the entry), and the mail
    // is stored with the entry's stamp (the channel) in one save.
    [TestCase(120, 1)]
    [TestCase(10, 0)]
    public void ReturnUser_LoadPlayer(int daysAway, int mails) {
        var test = new TestGameSession();
        DateTime lastSave = DateTime.UtcNow.AddDays(-daysAway);
        using (var context = new Ms2Context(test.Options)) {
            Maple2.Database.Model.Character row = context.Character.Single(character => character.Id == test.CharacterId);
            context.Entry(row).Property(character => character.LastModified).CurrentValue = lastSave;
            context.SaveChanges();
        }
        GameEventMetadata[] running = [
            Row(10100100, GameEventType.SendMail, new SendMailEvent("ReturnUser", 170531002, MailType.Notice, 90)),
        ];
        long seen = -1;
        using (GameStorage.Request db = test.Storage.Context()) {
            Player? player = db.LoadPlayer(test.AccountId, test.CharacterId, 1, 7, last => {
                seen = last;
                return EventMailRules.ReturnUser(running, test.CharacterId, last, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            });
            Assert.That(player, Is.Not.Null);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.Multiple(() => {
                Assert.That(seen, Is.EqualTo(new DateTimeOffset(lastSave).ToUnixTimeSeconds()).Within(1));
                Assert.That(db.GetAllMail(test.CharacterId).Select(mail => (mail.Type, mail.Content)),
                    Is.EqualTo(Enumerable.Repeat((MailType.Notice, "170531002"), mails)));
            });
        }
        using (var context = new Ms2Context(test.Options)) {
            Assert.That(context.Character.Single(character => character.Id == test.CharacterId).Channel, Is.EqualTo(7));
        }
    }

    // KR 180005: value1 170531001, value2 "20303031,1,1,0" (id, count, rank, unread), value3 "30,90".
    [Test]
    public void YearRoundReader_KrRow() {
        Assert.That(EventMailReader.YearRound("170531001", "20303031,1,1,0", "30,90"),
            Is.EqualTo(new ReturnUserYearRound(170531001, new RewardItem(20303031, 1, 1), 30, 90)));
        Assert.That(EventMailReader.YearRound(" 160616101 ", "20300284, 2, 3", "60"),
            Is.EqualTo(new ReturnUserYearRound(160616101, new RewardItem(20300284, 3, 2), 60, 0)));
    }

    // No mail, no readable item, or no days away is not stored.
    [TestCase(null, "20303031,1,1,0", "30,90")]
    [TestCase("0", "20303031,1,1,0", "30,90")]
    [TestCase("170531001", null, "30,90")]
    [TestCase("170531001", "20303031,1", "30,90")]
    [TestCase("170531001", "0,1,1,0", "30,90")]
    [TestCase("170531001", "20303031,0,1,0", "30,90")]
    [TestCase("170531001", "20303031,1,x,0", "30,90")]
    [TestCase("170531001", "20303031,1,1,0", null)]
    [TestCase("170531001", "20303031,1,1,0", "0,90")]
    [TestCase("170531001", "20303031,1,1,0", "x,90")]
    public void YearRoundReader_Unusable(string? value1, string? value2, string? value3) {
        Assert.That(EventMailReader.YearRound(value1, value2, value3), Is.Null);
    }

    // The client has no GameEventReturnUserYearRound class: the event list leaves it out.
    [Test]
    public void YearRound_NotSentToClient() {
        Assert.That(new GameEvent(Row(180005, GameEventType.ReturnUserYearRound, YearRoundRow)).SentToClient, Is.False);
    }

    private static readonly ReturnUserYearRound YearRoundRow = new(170531001, new RewardItem(20303031, 1, 1), 30, 90);

    // 30 days away gives the box once the account's last gift of the row is 90 days old (or there is none); less
    // away, a gift inside the wait, or a character never saved gets none.
    [Test]
    public void YearRound_AwayAndWait() {
        GameEventMetadata[] running = [
            Row(180005, GameEventType.ReturnUserYearRound, YearRoundRow),
            Row(10100100, GameEventType.SendMail, new SendMailEvent("ReturnUser", 170531002, MailType.Notice, 90)),
        ];
        Assert.Multiple(() => {
            EventMailRules.YearRoundGift[] gifts = EventMailRules.YearRound(running, CharacterId, Now - 30 * Day, Now, _ => null).ToArray();
            Assert.That(gifts.Select(gift => (gift.EventId, gift.Mail.Type, gift.Mail.ReceiverId, gift.Mail.Content, gift.Item, gift.WaitDays)),
                Is.EqualTo(new[] { (180005, MailType.System, CharacterId, "170531001", new RewardItem(20303031, 1, 1), 90) }));
            Assert.That(EventMailRules.YearRound(running, CharacterId, Now - 30 * Day + 1, Now, _ => null), Is.Empty);
            Assert.That(EventMailRules.YearRound(running, CharacterId, 0, Now, _ => null), Is.Empty);
            Assert.That(EventMailRules.YearRound(running, CharacterId, Now - 30 * Day, Now, id => id == 180005 ? Now - 90 * Day : null).Count(), Is.EqualTo(1));
            Assert.That(EventMailRules.YearRound(running, CharacterId, Now - 30 * Day, Now, id => id == 180005 ? Now - 90 * Day + 1 : null), Is.Empty);
            Assert.That(EventMailRules.YearRound(running, CharacterId, Now - 30 * Day, Now, id => id == 999 ? Now : null).Count(), Is.EqualTo(1));
        });
    }

    // Stored: the mail with its item and the account's gift time; the next entry inside the wait gets none, also
    // with another character of the account.
    [Test]
    public void YearRound_StoreOnce() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(20303031));
        GameEventMetadata[] running = [Row(180005, GameEventType.ReturnUserYearRound, YearRoundRow)];
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(ReturnUserGift.Store(db, test.AccountId, test.CharacterId, running, now - 40 * Day, now, MakeItem), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Mail[] mails = db.GetAllMail(test.CharacterId).ToArray();
            Assert.That(mails.Select(mail => (mail.Type, mail.Content)), Is.EqualTo(new[] { (MailType.System, "170531001") }));
            GameEventUserValue time = db.GetAccountEventValues(test.AccountId).Single();
            Assert.Multiple(() => {
                Assert.That((time.EventId, time.Type, time.Value), Is.EqualTo((180005, GameEventUserValueType.ReturnUserGiftTime, now.ToString())));
                Assert.That(time.ExpirationTime, Is.EqualTo(now + 90 * Day));
            });
            Assert.That(ReturnUserGift.Store(db, test.AccountId, test.CharacterId + 1, running, now - 40 * Day, now + 10 * Day, MakeItem), Is.True);
        }
        using (var context = new Ms2Context(test.Options)) {
            long mailId = context.Mail.Single().Id;
            Assert.That(context.Item.Where(item => item.OwnerId == mailId).Select(item => item.ItemId).ToArray(), Is.EqualTo(new[] { 20303031 }));
            Assert.That(context.Mail.Count(), Is.EqualTo(1));
        }
        Assert.That(GameEventManager.AccountValue(GameEventUserValueType.ReturnUserGiftTime), Is.True);
    }

    // A write that fails answers false (the load is not committed); an item that cannot be made is left out unmarked.
    [Test]
    public void YearRound_StoreFailures() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(20303031));
        GameEventMetadata[] running = [Row(180005, GameEventType.ReturnUserYearRound, YearRoundRow)];
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(ReturnUserGift.Store(db, test.AccountId, test.CharacterId, running, now - 40 * Day, now, _ => null), Is.True);
            Assert.That(db.GetAccountEventValues(test.AccountId), Is.Empty);
        }
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.AccountEventValue>().Any(entry => entry.State == EntityState.Added);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(ReturnUserGift.Store(db, test.AccountId, test.CharacterId, running, now - 40 * Day, now, MakeItem), Is.False);
        }
    }

    private static Item MakeItem(RewardItem reward) => new(TestGameSession.Metadata(reward.ItemId), reward.Rarity, reward.Amount);
}

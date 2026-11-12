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
}

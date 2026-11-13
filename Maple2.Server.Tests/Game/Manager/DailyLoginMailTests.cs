using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The daily login mail (DailyLoginReward): the row's items in one system mail with the row's text, once a day.
public class DailyLoginMailTests {
    private const int EventId = 7301;
    private const int MailText = 171228005;
    private const int Ticket = 20000011;
    private const int Bonus = 20000012;

    private static TestGameSession Test() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Ticket), TestGameSession.Metadata(Bonus));
        test.AddEvent(EventId, GameEventType.DailyLoginReward, new DailyLoginReward(MailText, [
            new RewardItem(Ticket, 1, 15),
            new RewardItem(Bonus, 1, 1),
        ]));
        return test;
    }

    private static (string Content, int[] Items, int Amount)[] Mails(TestGameSession test) {
        using var context = new Ms2Context(test.Options);
        long[] mailIds = context.Mail.Where(mail => mail.ReceiverId == test.CharacterId).Select(mail => mail.Id).ToArray();
        using var db = test.Storage.Context();
        return mailIds.Select(id => db.GetMail(id, test.CharacterId)!)
            .Select(mail => (mail.Content, mail.Items.Select(item => item.Id).OrderBy(id => id).ToArray(), mail.Items.Sum(item => item.Amount)))
            .ToArray();
    }

    [Test]
    public void DailyLoginReward_MailsTheItemsOnceADay() {
        TestGameSession test = Test();
        GameSession session = test.Start();

        session.GameEvent.Update(1000);
        session.GameEvent.Update(5000);

        Assert.That(Mails(test), Is.EqualTo(new[] { (MailText.ToString(), new[] { Ticket, Bonus }, 16) }));
        // The day's mark is stored with the mail, not at logout.
        Assert.That(test.StoredEventValues(EventId).GetValueOrDefault(GameEventUserValueType.DailyLoginRewardGiven), Is.EqualTo("1"));
        Assert.That(test.Errors, Is.Empty);
    }

    // Today's mark stored before a restart: the next login does not mail again.
    [Test]
    public void DailyLoginReward_StoredMark_NoSecondMail() {
        TestGameSession test = Test();
        using (var context = new Ms2Context(test.Options)) {
            context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                CharacterId = test.CharacterId, EventId = EventId, Type = GameEventUserValueType.DailyLoginRewardGiven, Value = "1",
                ExpirationTime = GameEventManager.DtRewardExpiry(DateTime.Now),
            });
            context.SaveChanges();
        }
        GameSession session = test.Start();

        session.GameEvent.Update(1000);

        Assert.That(Mails(test), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // value1 the mail text, value2 the items: a row with an unreadable item keeps the others, one without items is
    // not read.
    [Test]
    public void DailyLoginReward_IngestReadsTheItems() {
        Assert.That(ServerTableMapper.ReadDailyLoginReward("170928009", "30000898,100,1,0|x,1,1,0|50400241,1,3,0") is { } reward
                    && reward.MailId == 170928009
                    && reward.Items.SequenceEqual([new RewardItem(30000898, 1, 100), new RewardItem(50400241, 3, 1)]), Is.True);
        Assert.That(ServerTableMapper.ReadDailyLoginReward("", "20300322,1,1,0")?.MailId, Is.EqualTo(0));
        Assert.That(ServerTableMapper.ReadDailyLoginReward("180214016", ""), Is.Null);
    }

    // The play-time reward (DTReward): the next row's index is stored with the mail, not at logout, so a restart
    // after the mail does not give the row again.
    [Test]
    public void DtReward_IndexIsStoredWithTheMail() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Ticket));
        test.AddEvent(EventId, GameEventType.DTReward, new DTReward([
            new DTReward.Entry(StartDuration: 0, EndDuration: 1, MailContentId: MailText, Item: new RewardItem(Ticket, 1, 3)),
            new DTReward.Entry(StartDuration: 0, EndDuration: 600, MailContentId: MailText, Item: new RewardItem(Ticket, 1, 3)),
        ]));
        GameSession session = test.Start();

        session.GameEvent.Update(1000);

        Assert.That(Mails(test), Is.EqualTo(new[] { (MailText.ToString(), new[] { Ticket }, 3) }));
        Dictionary<GameEventUserValueType, string> stored = test.StoredEventValues(EventId);
        Assert.That(stored.GetValueOrDefault(GameEventUserValueType.DTRewardRewardIndex), Is.EqualTo("1"));
        Assert.That(stored.GetValueOrDefault(GameEventUserValueType.DTRewardCurrentTime), Is.EqualTo("0"));
        Assert.That(test.Errors, Is.Empty);
    }
}

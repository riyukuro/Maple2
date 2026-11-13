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

    private static TestGameSession Test(int minLevel = 0) {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Ticket), TestGameSession.Metadata(Bonus));
        test.AddEvent(EventId, GameEventType.DailyLoginReward, new DailyLoginReward(MailText, [
            new RewardItem(Ticket, 1, 15),
            new RewardItem(Bonus, 1, 1),
        ], minLevel));
        return test;
    }

    // The account's stored values of the event.
    private static Dictionary<GameEventUserValueType, string> StoredAccountValues(TestGameSession test) {
        using var db = test.Storage.Context();
        return db.GetAccountEventValues(test.AccountId).Where(value => value.EventId == EventId)
            .ToDictionary(value => value.Type, value => value.Value);
    }

    private static void StoreAccountMark(TestGameSession test, long expiry) {
        var mark = new GameEventUserValue(GameEventUserValueType.DailyLoginRewardGiven, expiry, EventId);
        mark.SetValue("1");
        using var db = test.Storage.Context();
        Assert.That(db.SaveAccountEventValues(test.AccountId, [mark]), Is.True);
    }

    // A clock that steps through `times`, one a read, and stays at the last.
    private static Func<DateTime> Steps(params DateTime[] times) {
        var queue = new Queue<DateTime>(times);
        return () => queue.Count > 1 ? queue.Dequeue() : queue.Peek();
    }

    // The last millisecond of today, then midnight and the seconds after it.
    private static DateTime[] AcrossMidnight() {
        DateTime midnight = DateTime.Today.AddDays(1);
        return [midnight.AddMilliseconds(-1), .. Enumerable.Range(0, 8).Select(second => midnight.AddSeconds(second))];
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
        // The day's mark is stored with the mail, not at logout, and for the account, not the character.
        Assert.That(StoredAccountValues(test).GetValueOrDefault(GameEventUserValueType.DailyLoginRewardGiven), Is.EqualTo("1"));
        Assert.That(test.StoredEventValues(EventId), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // Today's account mark stored before a restart, by this character or another of the account: the next login
    // does not mail again.
    [Test]
    public void DailyLoginReward_StoredAccountMark_NoSecondMail() {
        TestGameSession test = Test();
        StoreAccountMark(test, GameEventManager.DtRewardExpiry(DateTime.Now));
        GameSession session = test.Start();

        session.GameEvent.Update(1000);

        Assert.That(Mails(test), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // A step at the day's last millisecond that reads the clock again after midnight would find the day's mark
    // run out, mail, and store the new mark with the old day's end; the next step would mail again. One clock read
    // a step: the old day's mark holds, then midnight mails the new day once.
    [Test]
    public void DailyLoginReward_AcrossMidnight_OneMailForTheNewDay() {
        TestGameSession test = Test();
        DateTime[] times = AcrossMidnight();
        StoreAccountMark(test, GameEventManager.DtRewardExpiry(times[0]));
        GameSession session = test.Start();
        session.GameEvent.Clock = Steps(times);

        for (int step = 1; step <= 4; step++) {
            session.GameEvent.Update(step * 1000);
        }

        Assert.That(Mails(test), Has.Length.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // value3's first field is the lowest level that gets the mail: below it nothing is mailed nor marked, so the
    // mail comes on the step after the level is reached.
    [Test]
    public void DailyLoginReward_BelowTheLevel_NoMailUntilReached() {
        TestGameSession test = Test(minLevel: 10);
        GameSession session = test.Start();
        session.Player.Value.Character.Level = 9;

        session.GameEvent.Update(1000);
        Assert.That(Mails(test), Is.Empty);
        Assert.That(StoredAccountValues(test), Is.Empty);

        session.Player.Value.Character.Level = 10;
        session.GameEvent.Update(2000);
        Assert.That(Mails(test), Has.Length.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // The client has no class for the event: the event list leaves it out.
    [Test]
    public void DailyLoginReward_NotSentToClient() {
        GameSession session = Test().Start();

        Assert.That(session.FindEvent(GameEventType.DailyLoginReward).Single().SentToClient, Is.False);
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

    // value3's first field: the lowest level; empty, 0 or unreadable is none.
    [Test]
    public void DailyLoginReward_IngestReadsTheLevel() {
        Assert.Multiple(() => {
            Assert.That(ServerTableMapper.ReadDailyLoginReward("", "20300322,1,1,0", "10,1")?.MinLevel, Is.EqualTo(10));
            Assert.That(ServerTableMapper.ReadDailyLoginReward("", "20300322,1,1,0", "60")?.MinLevel, Is.EqualTo(60));
            Assert.That(ServerTableMapper.ReadDailyLoginReward("", "20300322,1,1,0", "0")?.MinLevel, Is.EqualTo(0));
            Assert.That(ServerTableMapper.ReadDailyLoginReward("", "20300322,1,1,0", "")?.MinLevel, Is.EqualTo(0));
            Assert.That(ServerTableMapper.ReadDailyLoginReward("", "20300322,1,1,0", "x")?.MinLevel, Is.EqualTo(0));
        });
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

    // The play-time reward across midnight: the old day's index (row 0 given) holds at the day's last millisecond,
    // and the new day starts the rows once, so row 0 is mailed once, not each time the index is started again.
    [Test]
    public void DtReward_AcrossMidnight_RowOnceForTheNewDay() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Ticket));
        test.AddEvent(EventId, GameEventType.DTReward, new DTReward([
            new DTReward.Entry(StartDuration: 0, EndDuration: 1, MailContentId: MailText, Item: new RewardItem(Ticket, 1, 3)),
            new DTReward.Entry(StartDuration: 0, EndDuration: 600, MailContentId: MailText, Item: new RewardItem(Ticket, 1, 3)),
        ]));
        DateTime[] times = AcrossMidnight();
        long oldDay = GameEventManager.DtRewardExpiry(times[0]);
        using (var context = new Ms2Context(test.Options)) {
            foreach ((GameEventUserValueType type, string value) in new[] {
                         (GameEventUserValueType.DTRewardRewardIndex, "1"), (GameEventUserValueType.DTRewardCurrentTime, "0"),
                     }) {
                context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                    CharacterId = test.CharacterId, EventId = EventId, Type = type, Value = value, ExpirationTime = oldDay,
                });
            }
            context.SaveChanges();
        }
        GameSession session = test.Start();
        session.GameEvent.Clock = Steps(times);

        for (int step = 1; step <= 4; step++) {
            session.GameEvent.Update(step * 1000);
        }

        Assert.That(Mails(test), Has.Length.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }
}

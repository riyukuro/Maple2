using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The play-time gifts (PlayGift): each gift of the client's playgiftevent row is mailed once the player has played its
// requireMins since the last one.
public class PlayGiftTests {
    private const int EventId = 60100;
    private const int Box = 30000406;
    private const int Ticket = 20300130;

    private static readonly PlayGift.Gift[] Gifts = [
        new PlayGift.Gift(1, 1, 51000003, new RewardItem(Box, 1, 1)),
        new PlayGift.Gift(2, 2, 51000005, new RewardItem(Ticket, 1, 4)),
    ];

    private static TestGameSession Test() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Box), TestGameSession.Metadata(Ticket));
        test.AddEvent(EventId, GameEventType.PlayGift, new PlayGift(151015, Gifts));
        return test;
    }

    private static (string Content, int Item, int Amount)[] Mails(TestGameSession test) {
        using var context = new Ms2Context(test.Options);
        long[] mailIds = context.Mail.Where(mail => mail.ReceiverId == test.CharacterId).OrderBy(mail => mail.Id).Select(mail => mail.Id).ToArray();
        using var db = test.Storage.Context();
        return mailIds.Select(id => db.GetMail(id, test.CharacterId)!)
            .Select(mail => (mail.Content, mail.Items.Single().Id, mail.Items.Single().Amount))
            .ToArray();
    }

    [Test]
    public void Step_DueAtRequireMinutes() {
        Assert.Multiple(() => {
            Assert.That(GameEventManager.PlayGiftStep(Gifts, 0, 58, 1), Is.EqualTo((59L, false)));
            Assert.That(GameEventManager.PlayGiftStep(Gifts, 0, 59, 1), Is.EqualTo((60L, true)));
            Assert.That(GameEventManager.PlayGiftStep(Gifts, 1, 100, 19), Is.EqualTo((119L, false)));
            Assert.That(GameEventManager.PlayGiftStep(Gifts, 1, 100, 20), Is.EqualTo((120L, true)));
            // Past the last gift nothing is due.
            Assert.That(GameEventManager.PlayGiftStep(Gifts, 2, 5000, 1), Is.EqualTo((5000L, false)));
        });
    }

    // One minute of play mails the first gift, two more the second; then nothing more that day.
    [Test]
    public void Gifts_MailedInOrderByPlayTime() {
        TestGameSession test = Test();
        GameSession session = test.Start();
        DateTime start = DateTime.Today.AddHours(12);
        DateTime now = start;
        session.GameEvent.Clock = () => now;

        session.GameEvent.Update(1000); // the first step counts one second
        now = start.AddSeconds(58);
        session.GameEvent.Update(2000);
        Assert.That(Mails(test), Is.Empty);

        now = start.AddSeconds(59);
        session.GameEvent.Update(3000);
        Assert.That(Mails(test), Is.EqualTo(new[] { ("51000003", Box, 1) }));
        // The next index is stored with the mail, not at logout.
        Assert.That(test.StoredEventValues(EventId).GetValueOrDefault(GameEventUserValueType.PlayGiftIndex), Is.EqualTo("1"));

        now = start.AddSeconds(59 + 119);
        session.GameEvent.Update(4000);
        Assert.That(Mails(test), Has.Length.EqualTo(1));
        now = start.AddSeconds(59 + 120);
        session.GameEvent.Update(5000);
        Assert.That(Mails(test), Is.EqualTo(new[] { ("51000003", Box, 1), ("51000005", Ticket, 4) }));

        now = start.AddHours(3);
        session.GameEvent.Update(6000);
        Assert.That(Mails(test), Has.Length.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    // The client has no GameEventPlayGift class: the event list leaves it out.
    [Test]
    public void NotSentToClient() {
        GameSession session = Test().Start();

        Assert.That(session.FindEvent(GameEventType.PlayGift).Single().SentToClient, Is.False);
    }

    // table/playgiftevent.xml: gifts in giftID order, unreadable rows skipped, an event without gifts left out.
    [Test]
    public void Ingest_MapsTheClientTable() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <playGiftEvent eventID="151231">
                <item giftID="4" itemID="20300130" count="1" grade="1" requireMins="30" systemMailID="51000005" />
                <item giftID="1" itemID="30000466" count="4" grade="2" requireMins="30" systemMailID="51000005" />
                <item giftID="2" itemID="bad" count="4" grade="1" requireMins="30" systemMailID="51000005" />
              </playGiftEvent>
              <playGiftEvent eventID="160000">
                <item giftID="1" itemID="0" count="1" grade="1" requireMins="30" systemMailID="1" />
              </playGiftEvent>
            </ms2>
            """);

        var events = ServerTableMapper.MapPlayGiftEvents(document);

        Assert.That(events.Keys, Is.EquivalentTo(new[] { 151231 }));
        Assert.That(events[151231], Is.EqualTo(new[] {
            new PlayGift.Gift(1, 30, 51000005, new RewardItem(30000466, 2, 4)),
            new PlayGift.Gift(4, 30, 51000005, new RewardItem(20300130, 1, 1)),
        }));
    }
}

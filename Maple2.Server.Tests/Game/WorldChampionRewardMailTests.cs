using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The world champion reward mail at login: the seasons ended in the reward days the character dealt the season's
// damage in, with the items of the row of the last step cleared (KR row 20000001: item table 28) and the season's
// systemMailID, once. Made-up season of three steps.
public class WorldChampionRewardMailTests {
    private const int Coin = 30001124, Ticket = 30001125, MailId = 180510006;

    private TestGameSession test = null!;

    private static WorldChampionTable.Season Season(int id, DateTime end) {
        DateTime begin = end.AddDays(-3);
        return new WorldChampionTable.Season(id, 2000423, 100, begin, end, 1500, MailId, [
            new WorldChampionTable.Step(1, 23500110, 20000001),
            new WorldChampionTable.Step(2, 23500111, 20000002),
            new WorldChampionTable.Step(3, 23500112, 20000003),
        ]);
    }

    // stored: the season's saved step, and the character's damage.
    private void Start(WorldChampionTable.Season season, int step, long damage, bool featureOn = true) {
        test = new TestGameSession();
        if (!featureOn) {
            test.SetFeaturesOff(WorldChampionRules.Feature);
        }
        test.SetServerTable(new WorldChampionTable(new Dictionary<int, WorldChampionTable.Season> { [season.Id] = season }));
        test.SetTable(new RewardContentTable(
            new Dictionary<int, RewardContentTable.Base> {
                [20000001] = new(20000001, 0, 0, 0f, 0f, 28, 0),
                [20000002] = new(20000002, 0, 0, 0f, 0f, 29, 0),
            },
            new Dictionary<int, RewardContentTable.Item> {
                [28] = new(28, [new RewardContentTable.Item.Data(0, 0, [new RewardItem(Coin, 1, 10), new RewardItem(Ticket, 2, 1)])]),
                [29] = new(29, [new RewardContentTable.Item.Data(0, 0, [new RewardItem(Coin, 1, 25)])]),
            },
            new Dictionary<int, long>(), new Dictionary<int, Dictionary<int, long>>(), new Dictionary<int, long>()));
        test.Register(TestGameSession.Metadata(Coin), TestGameSession.Metadata(Ticket));
        using (Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveWorldChampion(season.Id, step, 0, [new WorldChampionDamage(test.AccountId, test.CharacterId, damage)]), Is.True);
        }
        test.Start();
    }

    private List<(string Content, List<(int Id, int Amount)> Items)> Mails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Where(mail => mail.ReceiverId == test.CharacterId).ToList()
            .Select(mail => (mail.Content, context.Item.AsNoTracking().Where(item => item.OwnerId == mail.Id).ToList()
                .Select(item => (item.ItemId, item.Amount)).OrderBy(item => item.ItemId).ToList()))
            .ToList();
    }

    // Step 2 out at the end: step 1 cleared, row 20000001; mailed once.
    [Test]
    public void MailsTheLastClearedStepsRowOnce() {
        Start(Season(1, DateTime.Now.AddDays(-1)), 2, 1500);
        WorldChampionHandler.MailRewards(test.Session);
        WorldChampionHandler.MailRewards(test.Session);
        Assert.Multiple(() => {
            Assert.That(Mails(), Is.EqualTo(new[] { (MailId.ToString(), new List<(int, int)> { (Coin, 10), (Ticket, 1) }) }).AsCollection);
            Assert.That(test.World.MailNotifications, Has.Count.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Under the season's damage, no step cleared, the reward days over, the season not ended a minute ago, or the
    // feature off: no mail.
    [TestCase(1499, 3, -1.0, true)]
    [TestCase(1500, 1, -1.0, true)]
    [TestCase(1500, 3, -8.0, true)]
    [TestCase(1500, 3, 0.0, true)]
    [TestCase(1500, 3, -1.0, false)]
    public void NoMail(long damage, int step, double endDays, bool featureOn) {
        Start(Season(1, DateTime.Now.AddDays(endDays)), step, damage, featureOn);
        WorldChampionHandler.MailRewards(test.Session);
        Assert.That(Mails(), Is.Empty);
    }
}

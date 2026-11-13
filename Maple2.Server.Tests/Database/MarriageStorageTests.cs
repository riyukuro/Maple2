using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The marriage row through GameStorage, on an in-memory database. Each partner loads its own view
// (Partner1 is the other character); a message saved by either partner must leave the row's columns in place.
public class MarriageStorageTests {
    private GameStorage storage = null!;
    private long first;
    private long second;
    private long marriageId;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        using GameStorage.Request db = storage.Context();
        first = Create(db, "first");
        second = Create(db, "second");
        Marriage? marriage = db.CreateMarriage(first, second);
        Assert.That(marriage, Is.Not.Null);
        marriageId = marriage!.Id;
    }

    // The partner lookup joins the account and its home plot, so each character gets a real account.
    private static long Create(GameStorage.Request db, string name) {
        Account account = db.CreateAccount(new Account { Username = name }, "pw");
        var character = new Character { AccountId = account.Id, Name = name, Mastery = new Mastery() };
        character.ReturnMaps.Push(2000062);
        Character? created = db.CreateCharacter(character);
        Assert.That(created, Is.Not.Null);
        return created!.Id;
    }

    private Marriage Load(long characterId) {
        using GameStorage.Request db = storage.Context();
        Marriage? marriage = db.GetMarriage(characterId);
        Assert.That(marriage, Is.Not.Null);
        return marriage!;
    }

    private static string MessageOf(Marriage marriage, long characterId) {
        return marriage.Partner1.CharacterId == characterId ? marriage.Partner1.Message : marriage.Partner2.Message;
    }

    [Test]
    public void MessageFromEitherPartner_KeepsOneRowAndTheWriters() {
        foreach (long writer in new[] { first, second, first }) {
            using GameStorage.Request db = storage.Context();
            Assert.That(db.SaveCoupleMessage(marriageId, writer, "from " + writer), Is.True);
        }

        foreach (long reader in new[] { first, second }) {
            Marriage view = Load(reader);
            Assert.That(view.Id, Is.EqualTo(marriageId));
            Assert.That(MessageOf(view, first), Is.EqualTo("from " + first));
            Assert.That(MessageOf(view, second), Is.EqualTo("from " + second));
        }
        using (GameStorage.Request db = storage.Context()) {
            MarriageInfo info = db.GetMarriageInfo(second);
            Assert.That(info.Partner1Name, Is.EqualTo("first"));
            Assert.That(info.Partner2Name, Is.EqualTo("second"));
        }
        // Row count: with the one row deleted, neither partner has a marriage left, so no second row was added.
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteMarriage(marriageId), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetMarriage(first), Is.Null);
            Assert.That(db.GetMarriage(second), Is.Null);
        }
    }

    [Test]
    public void StaleCopy_DoesNotWriteTheOtherMessageBack() {
        Marriage stale = Load(second); // held by the partner on another channel

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCoupleMessage(marriageId, first, "new"), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCoupleMessage(marriageId, second, "mine"), Is.True);
            CoupleMessageRule.Merge(stale, db.GetMarriage(second)!);
        }

        Assert.That(MessageOf(stale, first), Is.EqualTo("new"));
        Assert.That(MessageOf(stale, second), Is.EqualTo("mine"));
        Marriage view = Load(first);
        Assert.That(MessageOf(view, first), Is.EqualTo("new"));
        Assert.That(MessageOf(view, second), Is.EqualTo("mine"));
    }

    [Test]
    public void MessageOfAStrangerOrMissingRow_IsRefused() {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveCoupleMessage(marriageId, second + 100, "x"), Is.False);
        Assert.That(db.SaveCoupleMessage(marriageId + 1, first, "x"), Is.False);
    }

    private void Marry() {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SetMarried(marriageId, DateTime.Now), Is.True);
    }

    private bool AddMission(long characterId, long amount = 5) {
        using GameStorage.Request db = storage.Context();
        return db.AddWeddingMissionExp(marriageId, characterId, amount, DateTime.Now);
    }

    // Both partners complete the same mission: two entries, one per partner. The second stored partner is sent the
    // first one first, so it sees the first's entry as the groom's (11) and its own as the bride's (12); the first
    // stored partner sees the two the other way round.
    [Test]
    public void WeddingMission_BothPartnersAddAnEntryEach() {
        Marry();
        Assert.That(AddMission(first), Is.True);
        Assert.That(AddMission(second), Is.True);

        Marriage ofSecond = Load(second);
        Marriage ofFirst = Load(first);
        Assert.Multiple(() => {
            Assert.That(ofSecond.ExpHistory.Select(exp => (exp.Type, exp.Amount)), Is.EqualTo(new[] {
                (MarriageExpType.WeddingMissionGroom, 5L), (MarriageExpType.WeddingMissionBride, 5L),
            }));
            Assert.That(ofFirst.ExpHistory.Select(exp => (exp.Type, exp.Amount)), Is.EqualTo(new[] {
                (MarriageExpType.WeddingMissionBride, 5L), (MarriageExpType.WeddingMissionGroom, 5L),
            }));
            Assert.That(ofFirst.ExpHistory.All(exp => exp.Time > 0), Is.True);
        });
    }

    private bool UseCharm(long characterId, DateTime time) {
        using GameStorage.Request db = storage.Context();
        return db.AddWeddingExpItem(marriageId, characterId, 350, MarriageExpRules.Cooldown(MarriageExpLimit.month), time);
    }

    // A Blessing Charm (WeddingExpItem): 350 in the user's own line (9 the groom's, 10 the bride's as each partner is
    // sent them), again only 30 days after that partner's last use; the other partner's use does not count against it.
    // Before, the server took the charm and added nothing.
    [Test]
    public void BlessingCharm_OncePer30DaysPerPartner() {
        Marry();
        DateTime now = DateTime.Now;
        Assert.Multiple(() => {
            Assert.That(UseCharm(first, now), Is.True);
            Assert.That(UseCharm(second, now), Is.True);
            Assert.That(UseCharm(first, now.AddDays(29)), Is.False);
            Assert.That(UseCharm(first, now.AddDays(30)), Is.True);
        });

        Marriage ofSecond = Load(second);
        Assert.That(ofSecond.ExpHistory.Select(exp => (exp.Type, exp.Amount)), Is.EqualTo(new[] {
            (MarriageExpType.weddingExpItem, 350L), (MarriageExpType.WeddingExpItemBride, 350L), (MarriageExpType.weddingExpItem, 350L),
        }));
        Assert.That(Load(first).ExpHistory.Select(exp => exp.Type).First(), Is.EqualTo(MarriageExpType.WeddingExpItemBride));
    }

    [Test]
    public void BlessingCharm_OnlyAMarriageCountsIt() {
        Assert.That(UseCharm(first, DateTime.Now), Is.False); // engaged
        Marry();
        Assert.That(UseCharm(second + 100, DateTime.Now), Is.False);
        Assert.That(Load(first).ExpHistory, Is.Empty);
    }

    // An engagement, a stranger, a missing row or no amount add nothing (the filed states: MarriageExpRulesTests).
    [Test]
    public void WeddingMission_OnlyAMarriageCountsIt() {
        Assert.That(AddMission(first), Is.False); // engaged
        Marry();
        Assert.Multiple(() => {
            Assert.That(AddMission(second + 100), Is.False);
            Assert.That(AddMission(first, 0), Is.False);
            using (GameStorage.Request db = storage.Context()) {
                Assert.That(db.AddWeddingMissionExp(marriageId + 1, first, 5, DateTime.Now), Is.False);
            }
            Assert.That(Load(first).ExpHistory, Is.Empty);
        });
    }
}

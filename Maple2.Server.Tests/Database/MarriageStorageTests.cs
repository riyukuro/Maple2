using System;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The marriage row through GameStorage, on an in-memory database. Each partner loads its own view
// (Partner1 is the other character); a save from either view must leave the row's columns in place.
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
    public void SaveFromEitherView_KeepsTheRowAndTheWriters() {
        foreach (long writer in new[] { first, second, first }) {
            Marriage view = Load(writer);
            Assert.That(CoupleMessageRule.Apply(view, writer, "from " + writer), Is.True);
            view.Status = MaritalStatus.Married;
            using (GameStorage.Request db = storage.Context()) {
                Assert.That(db.SaveMarriage(view), Is.True);
            }
        }

        foreach (long reader in new[] { first, second }) {
            Marriage view = Load(reader);
            Assert.That(view.Id, Is.EqualTo(marriageId));
            Assert.That(view.Status, Is.EqualTo(MaritalStatus.Married));
            Assert.That(MessageOf(view, first), Is.EqualTo("from " + first));
            Assert.That(MessageOf(view, second), Is.EqualTo("from " + second));
        }
        using (GameStorage.Request db = storage.Context()) {
            // the row found by id is the one found by character: no second row was added
            Assert.That(db.GetMarriage(weddingId: marriageId + 1), Is.Null);
            MarriageInfo info = db.GetMarriageInfo(second);
            Assert.That(info.Partner1Name, Is.EqualTo("first"));
            Assert.That(info.Partner2Name, Is.EqualTo("second"));
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
}

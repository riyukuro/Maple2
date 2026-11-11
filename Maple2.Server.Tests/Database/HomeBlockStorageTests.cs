using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The home block rows through GameStorage, on an in-memory database. Each call uses its own request,
// as the handlers do, so a change that is not saved is not seen by the next one.
public class HomeBlockStorageTests {
    private const long Owner = 1;
    private GameStorage storage = null!;
    private long blockedCharacter;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        using GameStorage.Request db = storage.Context();
        var blocked = new Character { AccountId = 2, Name = "blocked", Mastery = new Mastery() };
        blocked.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(blocked);
        Assert.That(character, Is.Not.Null);
        blockedCharacter = character!.Id;
    }

    private HomeBlockEntry Create(string memo) {
        using GameStorage.Request db = storage.Context();
        HomeBlockEntry? entry = db.CreateHomeBlock(Owner, blockedCharacter, memo);
        Assert.That(entry, Is.Not.Null);
        return entry!;
    }

    private IList<HomeBlockEntry> Load() {
        using GameStorage.Request db = storage.Context();
        return db.GetHomeBlocks(Owner);
    }

    [Test]
    public void MemoEdit_IsSaved() {
        HomeBlockEntry entry = Create("old");

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.UpdateHomeBlockMemo(Owner, entry.Id, "new"), Is.True);
        }

        IList<HomeBlockEntry> rows = Load();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Memo, Is.EqualTo("new"));
    }

    [Test]
    public void MemoEdit_OfAnotherOwnersRow_IsRefused() {
        HomeBlockEntry entry = Create("old");

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.UpdateHomeBlockMemo(Owner + 1, entry.Id, "new"), Is.False);
            Assert.That(db.UpdateHomeBlockMemo(Owner, entry.Id + 1, "new"), Is.False);
        }

        Assert.That(Load()[0].Memo, Is.EqualTo("old"));
    }

    [Test]
    public void Remove_DeletesTheRow() {
        HomeBlockEntry entry = Create("old");

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.RemoveHomeBlock(Owner, entry.Id), Is.True);
        }

        Assert.That(Load(), Is.Empty);
    }
}

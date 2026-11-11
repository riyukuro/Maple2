using System;
using System.Collections.Generic;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The build budget and the helper list of a home, saved through GameStorage on an in-memory database. The
// row is read back through the context: GetHome also needs the map metadata.
public class HomeBuildBudgetStorageTests {
    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private long accountId;

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        using GameStorage.Request db = storage.Context();
        accountId = db.CreateAccount(new Account { Username = "owner" }, "password").Id;
    }

    private Home Load() {
        using var context = new Ms2Context(options);
        Home? home = context.Home.Find(accountId);
        Assert.That(home, Is.Not.Null);
        return home!;
    }

    [Test]
    public void ANewHome_HasNoBudgetAndNoHelpers() {
        Home home = Load();
        Assert.That(home.BuildMeso, Is.EqualTo(0));
        Assert.That(home.BuildMerat, Is.EqualTo(0));
        Assert.That(home.Builders, Is.Empty);
    }

    [Test]
    public void BudgetAndHelpers_AreSavedWithTheHome() {
        Home home = Load();
        home.BuildMeso = 5000;
        home.BuildMerat = 70;
        home.Builders.Add(42);
        home.Builders.Add(43);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveHome(home), Is.True);
        }

        Home loaded = Load();
        Assert.That(loaded.BuildMeso, Is.EqualTo(5000));
        Assert.That(loaded.BuildMerat, Is.EqualTo(70));
        Assert.That(loaded.Builders, Is.EqualTo(new List<long> { 42, 43 }));

        loaded.Builders.Remove(42);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveHome(loaded), Is.True);
        }
        Assert.That(Load().Builders, Is.EqualTo(new List<long> { 43 }));
    }
}

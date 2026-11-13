using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DbListing = Maple2.Database.Model.UgcMarketItem;

namespace Maple2.Server.Tests.Database;

// The free relist on MySQL: the migration's RelistKind and FreeRelistUsed columns, and FreeRelistUgcMarketItem's
// conditional write, which the in-memory store runs by another path. Runs only when MS2_TEST_GAME_DB holds the
// connection string of a scratch MySQL game database with the migrations applied (with oldguids=true, as the
// servers connect). The rows are made up and removed after each test.
public class UgcFreeRelistMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int Design = 11300003;

    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private long accountId;
    private long id;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the conditional write needs a real database: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        accountId = 900000000000L + Random.Shared.Next(1, 1000000);
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null) {
            return;
        }
        using var context = new Ms2Context(options);
        context.UgcMarketItem.Where(row => row.AccountId == accountId).ExecuteDelete();
    }

    private UgcMarketItem Game(UgcMarketListingStatus status, byte kind) {
        ItemMetadata metadata = TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1);
        return new UgcMarketItem(metadata) {
            Id = id,
            Price = 500,
            TabId = 1,
            Status = status,
            ListingEndTime = DateTime.Now.AddDays(-1).ToEpochSeconds(),
            PromotionEndTime = 0,
            SellerAccountId = accountId,
            SellerCharacterId = 1,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Id = 5001, Name = "Listed", Url = "item/ugc/listed.png" },
            Blueprint = new ItemBlueprint(),
            RelistKind = kind,
        };
    }

    private void Insert(UgcMarketListingStatus status, byte kind) {
        using var context = new Ms2Context(options);
        DbListing row = Game(status, kind);
        context.UgcMarketItem.Add(row);
        context.SaveChanges();
        id = row.Id;
    }

    private DbListing Row() {
        using var context = new Ms2Context(options);
        return context.UgcMarketItem.AsNoTracking().Single(row => row.Id == id);
    }

    private bool FreeRelist(long account, long end) {
        using GameStorage.Request db = storage.Context();
        return db.FreeRelistUgcMarketItem(id, account, end);
    }

    [Test]
    public void FreeRelistWorksOnce() {
        Insert(UgcMarketListingStatus.Expired, UgcMarketItem.FreeRelistKind);
        Assert.That(Row().FreeRelistUsed, Is.False);
        long end = DateTime.Now.AddDays(30).ToEpochSeconds();
        Assert.That(FreeRelist(accountId, end), Is.True);
        DbListing row = Row();
        Assert.Multiple(() => {
            Assert.That(row.Status, Is.EqualTo(UgcMarketListingStatus.Active));
            Assert.That(row.ListingEndTime.ToEpochSeconds(), Is.EqualTo(end));
            Assert.That(row.RelistKind, Is.EqualTo(0));
            Assert.That(row.FreeRelistUsed, Is.True);
        });

        // Expired again (as a later expiry stores it): the relist is spent.
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveUgcMarketItem(Game(UgcMarketListingStatus.Expired, UgcMarketItem.FreeRelistKind)), Is.True);
        }
        Assert.That(FreeRelist(accountId, end + 60), Is.False);
        Assert.That(Row().Status, Is.EqualTo(UgcMarketListingStatus.Expired));
    }

    // A save from a copy made before the relist (FreeRelistUsed false) leaves the stored flag set.
    [Test]
    public void StaleSaveKeepsTheRelistSpent() {
        Insert(UgcMarketListingStatus.Expired, UgcMarketItem.FreeRelistKind);
        Assert.That(FreeRelist(accountId, DateTime.Now.AddDays(30).ToEpochSeconds()), Is.True);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveUgcMarketItems([Game(UgcMarketListingStatus.Expired, UgcMarketItem.FreeRelistKind)]), Is.True);
        }
        Assert.That(Row().FreeRelistUsed, Is.True);
        Assert.That(Row().RelistKind, Is.EqualTo(UgcMarketItem.FreeRelistKind));
    }

    [Test]
    public void StoppedListingIsNotRelistedFree() {
        Insert(UgcMarketListingStatus.Expired, UgcMarketItem.StoppedKind);
        Assert.That(FreeRelist(accountId, DateTime.Now.AddDays(30).ToEpochSeconds()), Is.False);
        DbListing row = Row();
        Assert.That((row.Status, row.FreeRelistUsed), Is.EqualTo((UgcMarketListingStatus.Expired, false)));
    }

    [Test]
    public void ActiveListingIsNotRelistedFree() {
        Insert(UgcMarketListingStatus.Active, UgcMarketItem.FreeRelistKind);
        Assert.That(FreeRelist(accountId, DateTime.Now.AddDays(30).ToEpochSeconds()), Is.False);
        Assert.That(Row().FreeRelistUsed, Is.False);
    }

    [Test]
    public void OtherAccountCannotRelist() {
        Insert(UgcMarketListingStatus.Expired, UgcMarketItem.FreeRelistKind);
        Assert.That(FreeRelist(accountId + 1, DateTime.Now.AddDays(30).ToEpochSeconds()), Is.False);
        DbListing row = Row();
        Assert.That((row.Status, row.FreeRelistUsed), Is.EqualTo((UgcMarketListingStatus.Expired, false)));
    }
}

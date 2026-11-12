using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The black market's buy and take-back through GameStorage, each call on its own request as the servers do.
// The database is in memory unless MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game
// database; there the conditional update and delete run as one statement each.
public class BlackMarketStorageTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int ItemId = 20000005;
    private const long Price = 300;
    private const int Rounds = 30;

    private TestGameSession test = null!;
    private GameStorage storage = null!;
    private DbContextOptions options = null!;
    private long seller;
    private long buyerA;
    private long buyerB;
    private readonly List<long> listings = [];

    [SetUp]
    public void SetUp() {
        // The test session supplies the item and table data; its own in-memory rows are not used on MySQL.
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId));
        options = test.Options;
        storage = test.Storage;
        if (Connection != null) {
            options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
            object items = typeof(GameStorage).GetField("itemMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.Storage)!;
            storage = new GameStorage(options, (ItemMetadataStorage) items, null!, null!, null!, test.TableMetadata, test.ServerTableMetadata,
                NullLogger<GameStorage>.Instance, null!);
        }
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MailExpiryDays = 30 });
        // Ids no character has: the rows made here are found, and removed, by them.
        long unique = 7_000_000_000_000 + Random.Shared.NextInt64(1_000_000_000) * 10;
        seller = unique;
        buyerA = unique + 1;
        buyerB = unique + 2;
        listings.Clear();
        if (Connection != null) {
            return;
        }
        // An item row names its owner by id alone. The server's tables number their rows in ranges of their own;
        // the in-memory store numbers every table from 1, so the listing and mail ids are moved apart.
        using var context = new Ms2Context(options);
        var rows = Enumerable.Range(0, 2000).Select(_ => new Maple2.Database.Model.BlackMarketListing()).ToArray();
        context.BlackMarketListing.AddRange(rows);
        context.SaveChanges();
        context.BlackMarketListing.RemoveRange(rows);
        context.SaveChanges();
    }

    [TearDown]
    public void TearDown() {
        using var context = new Ms2Context(options);
        long[] mails = context.Mail.Where(mail => mail.ReceiverId == buyerA || mail.ReceiverId == buyerB).Select(mail => mail.Id).ToArray();
        long[] items = context.BlackMarketListing.Where(row => row.CharacterId == seller).Select(row => row.ItemUid).ToArray();
        context.Item.RemoveRange(context.Item.Where(item => item.ItemId == ItemId && (mails.Contains(item.OwnerId) || items.Contains(item.Id))));
        context.Item.RemoveRange(context.Item.Where(item => item.ItemId == ItemId && listings.Contains(item.OwnerId)));
        context.BlackMarketListing.RemoveRange(context.BlackMarketListing.Where(row => row.CharacterId == seller));
        context.Mail.RemoveRange(context.Mail.Where(mail => mails.Contains(mail.Id)));
        context.SaveChanges();
    }

    private BlackMarketListing List(int amount) {
        Item item;
        using (GameStorage.Request db = storage.Context()) {
            item = db.CreateItem(0, new Item(TestGameSession.Metadata(ItemId), amount: amount)) ?? throw new InvalidOperationException("item row not created");
        }
        using (GameStorage.Request db = storage.Context()) {
            BlackMarketListing listing = db.CreateBlackMarketingListing(new BlackMarketListing(item) {
                AccountId = seller,
                CharacterId = seller,
                Deposit = 10,
                ExpiryTime = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                Price = Price,
                Quantity = amount,
            }) ?? throw new InvalidOperationException("listing row not created");
            listings.Add(listing.Id);
            return listing;
        }
    }

    // The amount bought, 0 when refused.
    private int Buy(long listingId, int quantity, long buyer, long price = Price) {
        using GameStorage.Request db = storage.Context();
        BlackMarketListing? after = db.BuyBlackMarketListing(listingId, quantity, price, new Mail { ReceiverId = buyer }, out Mail? mail, out _);
        if (after == null) {
            Assert.That(mail, Is.Null);
            return 0;
        }
        Assert.That(mail!.Items.Sum(item => item.Amount), Is.EqualTo(quantity));
        return quantity;
    }

    private int Mailed(long buyer) {
        using GameStorage.Request db = storage.Context();
        return db.GetAllMail(buyer).SelectMany(mail => mail.Items).Where(item => item.Id == ItemId).Sum(item => item.Amount);
    }

    private int Listed(long listingId) {
        using GameStorage.Request db = storage.Context();
        return db.GetBlackMarketListing(listingId)?.Quantity ?? 0;
    }

    // A buy the listing cannot serve changes nothing: no mail, the count and the stack as they were.
    [TestCase(5, Price)]
    [TestCase(0, Price)]
    [TestCase(-1, Price)]
    [TestCase(2, Price - 1)]
    public void Buy_RefusedChangesNothing(int quantity, long price) {
        BlackMarketListing listing = List(4);

        Assert.That(Buy(listing.Id, quantity, buyerA, price), Is.EqualTo(0));
        Assert.That(Mailed(buyerA), Is.EqualTo(0));
        Assert.That(Listed(listing.Id), Is.EqualTo(4));
        using GameStorage.Request db = storage.Context();
        Assert.That(db.GetBlackMarketListing(listing.Id)!.Item.Amount, Is.EqualTo(4));
    }

    [Test]
    public void Buy_PartThenRest() {
        BlackMarketListing listing = List(4);

        Assert.That(Buy(listing.Id, 3, buyerA), Is.EqualTo(3));
        Assert.That(Listed(listing.Id), Is.EqualTo(1));
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetBlackMarketListing(listing.Id)!.Item.Amount, Is.EqualTo(1));
        }
        Assert.That(Buy(listing.Id, 1, buyerB), Is.EqualTo(1));
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetBlackMarketListing(listing.Id), Is.Null);
        }
        Assert.That(Buy(listing.Id, 1, buyerB), Is.EqualTo(0));
        Assert.That((Mailed(buyerA), Mailed(buyerB)), Is.EqualTo((3, 1)));
    }

    // Two buyers at once on a listing of four: together they never get more than four, and what is left is listed.
    [TestCase(3, 3)]
    [TestCase(4, 1)]
    [TestCase(2, 2)]
    public void Buy_TwoRacingNeverOversell(int first, int second) {
        int boughtA = 0;
        int boughtB = 0;
        for (int round = 0; round < Rounds; round++) {
            BlackMarketListing listing = List(4);
            int[] got = new int[2];
            Parallel.Invoke(
                () => got[0] = Buy(listing.Id, first, buyerA),
                () => got[1] = Buy(listing.Id, second, buyerB));

            Assert.That(got[0] + got[1], Is.EqualTo(first + second <= 4 ? first + second : Math.Max(got[0], got[1])), $"round {round}");
            Assert.That(got[0] + got[1], Is.GreaterThan(0).And.LessThanOrEqualTo(4), $"round {round}");
            Assert.That(Listed(listing.Id), Is.EqualTo(4 - got[0] - got[1]), $"round {round}");
            boughtA += got[0];
            boughtB += got[1];
            Assert.That((Mailed(buyerA), Mailed(buyerB)), Is.EqualTo((boughtA, boughtB)), $"round {round}");
        }
    }

    // A buyer and the seller's take-back at once: the four items end with the buyer or back with the seller,
    // never both and never neither.
    [Test]
    public void Buy_RacingTheTakeBack() {
        int bought = 0;
        for (int round = 0; round < Rounds; round++) {
            BlackMarketListing listing = List(4);
            int got = 0;
            BlackMarketListing? taken = null;
            Parallel.Invoke(
                () => got = Buy(listing.Id, 3, buyerA),
                () => {
                    using GameStorage.Request db = storage.Context();
                    taken = db.TakeBlackMarketListing(listing.Id, seller);
                });

            Assert.That(taken, Is.Not.Null, $"round {round}");
            Assert.That(got + taken!.Quantity, Is.EqualTo(4), $"round {round}");
            Assert.That(taken.Item.Amount, Is.EqualTo(taken.Quantity), $"round {round}");
            Assert.That(Listed(listing.Id), Is.EqualTo(0), $"round {round}");
            bought += got;
            Assert.That(Mailed(buyerA), Is.EqualTo(bought), $"round {round}");
            using GameStorage.Request other = storage.Context();
            Assert.That(other.TakeBlackMarketListing(listing.Id, seller), Is.Null, $"round {round}");
        }
    }

    [Test]
    public void Take_OnlyTheSellersOwn() {
        BlackMarketListing listing = List(4);

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.TakeBlackMarketListing(listing.Id, buyerA), Is.Null);
        }
        Assert.That(Listed(listing.Id), Is.EqualTo(4));
    }
}

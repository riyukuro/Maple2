using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// A black market buy on MySQL: the buyer's wallet, the listing's count, the item and both mails are one transaction
// (the purchase save), which the in-memory store cannot show. A throw after the buy's rows are written (before the
// commit) leaves none of them stored: the listing keeps its count, no mail, the stored wallet still holds the price.
// Runs only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database.
public class BlackMarketPurchaseMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int ItemId = 20000005;
    private const long Price = 300;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private DbContextOptions plain = null!;
    private IDisposable? held;
    private long sellerAccount;
    private long seller;
    private long listingId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the purchase save's rollback needs a real database: set MS2_TEST_GAME_DB");
        }
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).Options);
        ItemMetadata metadata = TestGameSession.Metadata(ItemId);
        test.Register(metadata);
        // As a session that took the account lock at its entry (GameSession.EnterServer).
        held = GameStorage.HoldOwners(test.AccountId, test.CharacterId);
        test.Start();
        session.Currency.Meso = 5000;
        Assume.That(session.SavePurchase(), Is.True, "the wallet stored before the buy");
        test.TakeSent();
        // The seller: an account and character of their own.
        var other = new TestGameSession(plain);
        sellerAccount = other.AccountId;
        seller = other.CharacterId;

        Item item;
        using (GameStorage.Request db = test.Storage.Context()) {
            item = db.CreateItem(0, new Item(metadata, amount: 4)) ?? throw new InvalidOperationException("item row not created");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            listingId = (db.CreateBlackMarketingListing(new BlackMarketListing(item) {
                AccountId = sellerAccount,
                CharacterId = seller,
                Deposit = 10,
                ExpiryTime = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                Price = Price,
                Quantity = 4,
            }) ?? throw new InvalidOperationException("listing row not created")).Id;
        }
    }

    [TearDown]
    public void TearDown() {
        GameStorage.Request.OnBuyStored = null;
        held?.Dispose();
        held = null;
        if (Connection == null) {
            return;
        }
        using var context = new Ms2Context(plain);
        context.Mail.Where(mail => mail.ReceiverId == seller || mail.ReceiverId == test.CharacterId).ExecuteDelete();
        context.BlackMarketListing.Where(listing => listing.AccountId == sellerAccount).ExecuteDelete();
    }

    private void Buy(int quantity) {
        new BlackMarketHandler { TableMetadata = test.TableMetadata, World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(listingId);
            writer.WriteInt(quantity);
        }));
    }

    // (stored wallet, the listing's stored count, the buyer's and the seller's mails)
    private (long Meso, int? Quantity, int Mails) Stored() {
        using var context = new Ms2Context(plain);
        long meso = context.Character.AsNoTracking().Single(row => row.Id == test.CharacterId).Currency.Meso;
        int? quantity = context.BlackMarketListing.AsNoTracking().Where(row => row.Id == listingId).Select(row => (int?) row.Quantity).SingleOrDefault();
        int mails = context.Mail.AsNoTracking().Count(mail => mail.ReceiverId == seller || mail.ReceiverId == test.CharacterId);
        return (meso, quantity, mails);
    }

    // The buy stores the wallet with the sale: the price is out of the stored wallet at once.
    [Test]
    public void Buy_StoresTheWalletWithTheSale() {
        Buy(1);

        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - Price));
        Assert.That(Stored(), Is.EqualTo((5000 - Price, (int?) 3, 2)));
    }

    // A throw after the buy's rows are written, before the purchase save commits: all of it is rolled back, the
    // price comes back to the session's wallet, and the answer is the one of a buy the server could not make.
    [Test]
    public void Buy_AThrowBeforeTheCommitStoresNothing() {
        GameStorage.Request.OnBuyStored = id => {
            if (id == listingId) {
                throw new InvalidOperationException("before the commit");
            }
        };

        Buy(1);
        GameStorage.Request.OnBuyStored = null;

        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(Stored(), Is.EqualTo((5000L, (int?) 4, 0)));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_purchase_count, 5, listingId).ToArray()),
        }));

        Buy(1);
        Assert.That(Stored(), Is.EqualTo((5000 - Price, (int?) 3, 2)));
    }
}

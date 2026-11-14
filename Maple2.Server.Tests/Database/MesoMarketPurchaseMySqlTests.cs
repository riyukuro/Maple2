using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Database;

// A meso market buy on MySQL: the buyer's wallet, the listing's end and both mails are one transaction (the purchase
// save), which the in-memory store cannot show. A save that fails after the listing's delete and the wallet's write
// (here: at the mails) leaves none of them stored: the listing is still for sale, no mail, the stored wallet still
// holds the price. Runs only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database.
public class MesoMarketPurchaseMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const long BASE = 5000000;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private DbContextOptions plain = null!;
    private readonly MailFault fault = new();
    private IDisposable? held;
    private long sellerAccount;
    private long seller;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the purchase save's rollback needs a real database: set MS2_TEST_GAME_DB");
        }
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(fault).Options);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            MesoMarketBasePrice = (int) BASE,
            MesoMarketProductUnit0 = (int) BASE,
            MesoMarketBuyPayType = 16,
            MailExpiryDays = 30,
        });
        // As a session that took the account lock at its entry (GameSession.EnterServer).
        held = GameStorage.HoldOwners(test.AccountId, test.CharacterId);
        test.Start();
        session.Currency[CurrencyType.MesoToken] = 500;
        Assume.That(session.SavePurchase(), Is.True, "the wallet stored before the buy");
        test.TakeSent();
        // The seller: an account and character of their own (the listing's and the mail's rows need them).
        var other = new TestGameSession(plain);
        sellerAccount = other.AccountId;
        seller = other.CharacterId;
    }

    [TearDown]
    public void TearDown() {
        held?.Dispose();
        held = null;
        if (Connection == null) {
            return;
        }
        fault.Fail = false;
        using var context = new Ms2Context(plain);
        context.Mail.Where(mail => mail.ReceiverId == seller || mail.ReceiverId == test.CharacterId).ExecuteDelete();
        context.MesoMarket.Where(listing => listing.AccountId == sellerAccount).ExecuteDelete();
        context.MesoMarketSold.Where(listing => listing.AccountId == sellerAccount).ExecuteDelete();
    }

    private MesoListing Listing() {
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateMesoListing(new MesoListing(TimeSpan.FromDays(2)) {
            AccountId = sellerAccount, CharacterId = seller, Amount = 2 * BASE, Price = 420,
        })!;
    }

    private void Buy(long listingId) {
        var handler = new MesoMarketHandler { ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteLong(listingId);
        }));
    }

    private (long Tokens, bool Listed, int Mails) Stored(long listingId) {
        using var context = new Ms2Context(plain);
        long tokens = context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).Currency.MesoToken;
        bool listed = context.MesoMarket.AsNoTracking().Any(listing => listing.Id == listingId);
        int mails = context.Mail.AsNoTracking().Count(mail => mail.ReceiverId == seller || mail.ReceiverId == test.CharacterId);
        return (tokens, listed, mails);
    }

    // The buy stores the wallet with the sale: the price is out of the stored wallet at once, not at the next
    // session save.
    [Test]
    public void Buy_StoresTheWalletWithTheSale() {
        MesoListing listing = Listing();

        Buy(listing.Id);

        Assert.That(session.Currency[CurrencyType.MesoToken], Is.EqualTo(80));
        Assert.That(Stored(listing.Id), Is.EqualTo((80L, false, 2)));
    }

    // The mails' write fails after the listing's delete and the wallet's write: all of it is rolled back. The
    // price comes back to the session's wallet, and the answer is the sold-out one.
    [Test]
    public void Buy_FailedSaveStoresNothing() {
        MesoListing listing = Listing();
        fault.Fail = true;

        Buy(listing.Id);
        fault.Fail = false;

        Assert.That(session.Currency[CurrencyType.MesoToken], Is.EqualTo(500));
        Assert.That(Stored(listing.Id), Is.EqualTo((500L, true, 0)));
        Assert.That(session.Player.Value.Account.MesoMarketPurchased, Is.EqualTo(0));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.MesoMarket)
            .Select(packet => Convert.ToHexString(packet)[4..6]), Is.EqualTo(new[] { "08" }));
    }

    // Fails a save that adds a mail: the sale's second write, after the listing's delete.
    private sealed class MailFault : SaveChangesInterceptor {
        public bool Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (Fail && eventData.Context != null &&
                eventData.Context.ChangeTracker.Entries<Maple2.Database.Model.Mail>().Any(entry => entry.State == EntityState.Added)) {
                throw new DbUpdateException("The test made this save fail");
            }
            return result;
        }
    }
}

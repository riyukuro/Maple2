using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MesoMarketHandler: the meso pages of the black market window (register, take back, search, buy).
public class MesoMarketHandlerTests {
    private const long BASE = 5000000;
    private const long OTHER_ACCOUNT = 777;
    private const long OTHER_CHARACTER = 778;

    private TestGameSession test = null!;
    private MesoMarketHandler handler = null!;

    private GameSession Login(long meso = 0, long tokens = 0) {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            MesoMarketBasePrice = (int) BASE,
            MesoMarketProductUnit0 = (int) BASE,
            MesoMarketProductUnit1 = (int) (2 * BASE),
        });
        handler = new MesoMarketHandler { ServerTableMetadata = test.ServerTableMetadata };
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        session.Currency[CurrencyType.MesoToken] = tokens;
        test.TakeSent();
        return session;
    }

    private void Send(GameSession session, byte mode, params long[] values) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            foreach (long value in values) {
                writer.WriteLong(value);
            }
        }));
    }

    // The packets' bodies after the opcode, as hex.
    private List<string> Sent() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.MesoMarket)
            .Select(packet => Convert.ToHexString(packet)[4..]).ToList();
    }

    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));
    private static string Int(int value) => Convert.ToHexString(BitConverter.GetBytes(value));

    private MesoListing OtherListing(long amount, long price, int days = 2) {
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateMesoListing(new MesoListing(TimeSpan.FromDays(days)) {
            AccountId = OTHER_ACCOUNT,
            CharacterId = OTHER_CHARACTER,
            Amount = amount,
            Price = price,
        })!;
    }

    private ICollection<MesoListing> OwnListings(GameSession session) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMyMesoListingsByAccountId(session.AccountId);
    }

    // Mode 5: the listing with the own flag and the day's count, then the counts; the listed meso leave.
    [TestCase(1, 200)]
    [TestCase(1, 160)]
    [TestCase(1, 240)]
    [TestCase(2, 400)]
    public void Register_TakesTheListedMeso(int units, long price) {
        GameSession session = Login(meso: 3 * BASE);

        Send(session, 5, units * BASE, price);

        MesoListing listing = OwnListings(session).Single();
        Assert.That((listing.Amount, listing.Price), Is.EqualTo((units * BASE, price)));
        Assert.That(session.Currency.Meso, Is.EqualTo((3 - units) * BASE));
        List<string> sent = Sent();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(sent[0], Does.StartWith("05" + Long(listing.Id) + Long(units * BASE) + Long(price)));
        Assert.That(sent[0], Does.EndWith("01" + Int(1)));
        Assert.That(sent[1], Is.EqualTo("02" + Int(1) + Int(0)));
    }

    // Mode 5 refused: mode 0 with the code and nothing taken. 15 an amount that is no product unit, 5 too few
    // meso, 16 a price per base price outside the range or a price that is no whole count of it.
    [TestCase(7000000L, 200L, 3 * BASE, 15)]
    [TestCase(3 * BASE, 600L, 3 * BASE, 15)]
    [TestCase(0L, 200L, 3 * BASE, 15)]
    [TestCase(BASE, 200L, BASE - 1, 5)]
    [TestCase(BASE, 241L, 3 * BASE, 16)]
    [TestCase(BASE, 159L, 3 * BASE, 16)]
    [TestCase(2 * BASE, 200L, 3 * BASE, 16)]
    [TestCase(2 * BASE, 401L, 3 * BASE, 16)]
    public void Register_Refused(long amount, long price, long meso, int code) {
        GameSession session = Login(meso: meso);

        Send(session, 5, amount, price);

        // Code 16 comes after the limits (mode 1) with the average of now.
        List<string> sent = Sent();
        Assert.That(sent.Select(packet => packet[..2]), Is.EqualTo(code == 16 ? new[] { "01", "00" } : new[] { "00" }));
        Assert.That(sent[^1], Is.EqualTo("00" + Int(code)));
        Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        Assert.That(OwnListings(session), Is.Empty);
    }

    // Five listings at once and five a day: the sixth is refused with 8, and with 13 once one was taken back.
    [Test]
    public void Register_Limits() {
        GameSession session = Login(meso: 10 * BASE);
        for (int i = 0; i < 5; i++) {
            Send(session, 5, BASE, 200);
        }
        Assert.That(OwnListings(session), Has.Count.EqualTo(5));
        test.TakeSent();

        Send(session, 5, BASE, 200);
        Assert.That(Sent(), Is.EqualTo(new[] { "00" + Int(8) }));

        long id = OwnListings(session).First().Id;
        Send(session, 6, id);
        Assert.That(Sent(), Is.EqualTo(new[] { "06" + Int(0) + Long(id) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(6 * BASE));

        Send(session, 5, BASE, 200);
        Assert.That(Sent(), Is.EqualTo(new[] { "00" + Int(13) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(6 * BASE));
    }

    // Mode 6 on another account's listing: 9, and the listing stays.
    [Test]
    public void TakeBack_OnlyTheOwnListing() {
        GameSession session = Login();
        MesoListing other = OtherListing(BASE, 200);

        Send(session, 6, other.Id);

        Assert.That(Sent(), Is.EqualTo(new[] { "00" + Int(9) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(0));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetMesoListing(other.Id), Is.Not.Null);
    }

    // Mode 7: the listings in the amount range, cheapest first, each with its own flag.
    [Test]
    public void Search_ListsTheRange() {
        GameSession session = Login(meso: BASE);
        MesoListing dear = OtherListing(BASE, 230);
        MesoListing cheap = OtherListing(BASE, 170);
        OtherListing(2 * BASE, 400);
        Send(session, 5, BASE, 200);
        long own = OwnListings(session).Single().Id;
        test.TakeSent();

        Send(session, 7, BASE, BASE);

        string list = Sent().Single();
        Assert.That(list, Does.StartWith("07" + Int(3) + Long(cheap.Id)));
        Assert.That(list.IndexOf(Long(own) + Long(BASE) + Long(200), StringComparison.Ordinal), Is.EqualTo(2 + 8 + 82));
        Assert.That(list.Substring(2 + 8 + 80, 2), Is.EqualTo("00"));
        Assert.That(list.Substring(2 + 8 + 82 + 80, 2), Is.EqualTo("01"));
        Assert.That(list.Substring(2 + 8 + 164, 16), Is.EqualTo(Long(dear.Id)));

        Send(session, 7, 0, BASE);
        Assert.That(Sent(), Is.EqualTo(new[] { "00" + Int(10) }));
    }

    // Mode 8: the tokens leave, the buyer's mail holds the meso, the seller's mail the price less the 5% fee, and
    // the answer is the code, the listing and the month's count, then the counts.
    [Test]
    public void Buy_PaysTokensAndMailsBoth() {
        GameSession session = Login(tokens: 500);
        MesoListing listing = OtherListing(2 * BASE, 420);

        Send(session, 8, listing.Id);

        Assert.That(Sent(), Is.EqualTo(new[] { "08" + Int(0) + Long(listing.Id) + Int(1), "02" + Int(0) + Int(1) }));
        Assert.That(session.Currency[CurrencyType.MesoToken], Is.EqualTo(80));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetMesoListing(listing.Id), Is.Null);
        Mail buyer = db.GetAllMail(session.CharacterId).Single();
        Assert.That((buyer.Type, buyer.Meso, buyer.Meret), Is.EqualTo((MailType.MesoMarket, 2 * BASE, 0L)));
        Mail seller = db.GetAllMail(OTHER_CHARACTER).Single();
        Assert.That((seller.Type, seller.Meso, seller.Meret), Is.EqualTo((MailType.MesoMarket, 0L, 399L)));
        Assert.That(seller.ContentArgs.Select(arg => arg.Value), Is.EqualTo(new[] { "10000000", "420", "21", "5", "399" }));
        Assert.That(test.World.MailNotifications.Select(request => request.CharacterId),
            Is.EquivalentTo(new[] { session.CharacterId, OTHER_CHARACTER }));
    }

    // Mode 8 on a listing that is gone: mode 8 with code 17, on which the client drops the row from its list.
    [Test]
    public void Buy_GoneListingAnswersSoldOut() {
        GameSession session = Login(tokens: 500);

        Send(session, 8, 4242);

        Assert.That(Sent(), Is.EqualTo(new[] { "08" + Int(17) + Long(4242) + Int(0) }));
        Assert.That(session.Currency[CurrencyType.MesoToken], Is.EqualTo(500));
    }

    // Mode 8 on a listing past its end: code 17 as for a sold one, so the client drops the row; nothing is paid and
    // the listing stays for its owner, who can take it back.
    [Test]
    public void Buy_EndedListingAnswersSoldOut() {
        GameSession session = Login(tokens: 500);
        MesoListing listing = OtherListing(BASE, 200, -1);

        Send(session, 8, listing.Id);

        Assert.That(Sent(), Is.EqualTo(new[] { "08" + Int(17) + Long(listing.Id) + Int(0) }));
        Assert.That(session.Currency[CurrencyType.MesoToken], Is.EqualTo(500));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetMesoListing(listing.Id), Is.Not.Null);
        Assert.That(db.GetAllMail(session.CharacterId), Is.Empty);
    }

    // Mode 7 lists a listing until its end, whatever the host's time zone.
    [Test]
    public void Search_LeavesOutEndedListings() {
        GameSession session = Login();
        using (GameStorage.Request db = test.Storage.Context()) {
            foreach (int hours in new[] { -1, 1 }) {
                db.CreateMesoListing(new MesoListing(TimeSpan.FromHours(hours)) {
                    AccountId = OTHER_ACCOUNT, CharacterId = OTHER_CHARACTER, Amount = BASE, Price = 200 + hours,
                });
            }
        }

        Send(session, 7, BASE, BASE);

        string list = Sent().Single();
        Assert.That(list, Does.StartWith("07" + Int(1)));
        Assert.That(list, Does.Contain(Long(BASE) + Long(201)));
    }

    // Mode 8 refused: 7 too few tokens, 14 the month's count used up.
    [TestCase(199L, 2, 0, 7)]
    [TestCase(500L, 2, 10, 14)]
    public void Buy_Refused(long tokens, int days, int bought, int code) {
        GameSession session = Login(tokens: tokens);
        session.Player.Value.Account.MesoMarketPurchased = bought;
        MesoListing listing = OtherListing(BASE, 200, days);

        Send(session, 8, listing.Id);

        Assert.That(Sent(), Is.EqualTo(new[] { "00" + Int(code) }));
        Assert.That(session.Currency[CurrencyType.MesoToken], Is.EqualTo(tokens));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetMesoListing(listing.Id), Is.Not.Null);
        Assert.That(db.GetAllMail(session.CharacterId), Is.Empty);
    }

    // Mode 8 while the adventurer protection is on (here: below its level): code 20, the client's 0x14
    // (s_adventure_protect_err); nothing is paid or mailed.
    [Test]
    public void Buy_RefusedWhileProtected() {
        GameSession session = Login(tokens: 500);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureProtectRequireLevel = 200 });
        MesoListing listing = OtherListing(BASE, 200);

        Send(session, 8, listing.Id);

        Assert.That(Sent(), Is.EqualTo(new[] { "00" + Int(20) }));
        Assert.That(session.Currency[CurrencyType.MesoToken], Is.EqualTo(500));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetMesoListing(listing.Id), Is.Not.Null);
    }

    // The average price of the window's load follows the sales: 200 until 10 are sold, then the mean price per
    // base price of the sales, and the register range moves with it. A register the client let through with its
    // older average gets the limits with the average of now before the code 16.
    [Test]
    public void AveragePrice_FollowsTheSales() {
        GameSession session = Login(meso: BASE, tokens: 5000);
        for (int i = 0; i < 9; i++) {
            Send(session, 8, OtherListing(BASE, 160).Id);
        }
        test.TakeSent();
        Send(session, 3);
        Assert.That(Sent()[0].Substring(18, 16), Is.EqualTo(Long(200)));

        Send(session, 8, OtherListing(2 * BASE, 360).Id);
        test.TakeSent();
        Send(session, 3);
        string limits = Sent()[0];
        Assert.That(limits.Substring(18, 16), Is.EqualTo(Long(162)));

        Send(session, 5, BASE, 200);
        Assert.That(Sent(), Is.EqualTo(new[] { limits, "00" + Int(16) }));
        Send(session, 5, BASE, 190);
        Assert.That(OwnListings(session), Has.Count.EqualTo(1));
    }

    // A second player on the same database, with its own handler.
    private (TestGameSession Test, GameSession Session, MesoMarketHandler Handler) Second(long tokens) {
        var second = new TestGameSession(test.Options);
        second.SetServerTable(test.ServerTableMetadata.ConstantsTable);
        GameSession session = second.Start();
        session.Currency[CurrencyType.MesoToken] = tokens;
        second.TakeSent();
        return (second, session, new MesoMarketHandler { ServerTableMetadata = second.ServerTableMetadata });
    }

    private static void Buy(MesoMarketHandler handler, GameSession session, long listingId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteLong(listingId);
        }));
    }

    // Two buyers at once on one listing: one pays and gets the one mail, the other gets the sold-out answer and
    // keeps its tokens; the seller is paid once.
    [Test]
    public void Buy_TwoBuyersRacing() {
        for (int round = 0; round < 40; round++) {
            GameSession first = Login(tokens: 500);
            (TestGameSession otherTest, GameSession other, MesoMarketHandler otherHandler) = Second(500);
            MesoListing listing = OtherListing(BASE, 200);
            MesoMarketHandler firstHandler = handler;

            Parallel.Invoke(
                () => Buy(firstHandler, first, listing.Id),
                () => Buy(otherHandler, other, listing.Id));

            long[] paid = [500 - first.Currency[CurrencyType.MesoToken], 500 - other.Currency[CurrencyType.MesoToken]];
            Assert.That(paid.OrderBy(tokens => tokens), Is.EqualTo(new[] { 0L, 200L }), $"round {round}");
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.GetMesoListing(listing.Id), Is.Null, $"round {round}");
            long[] mailed = [db.GetAllMail(first.CharacterId).Sum(mail => mail.Meso), db.GetAllMail(other.CharacterId).Sum(mail => mail.Meso)];
            Assert.That(mailed, Is.EqualTo(paid.Select(tokens => tokens == 0 ? 0 : BASE)), $"round {round}");
            Assert.That(db.GetAllMail(OTHER_CHARACTER).Count, Is.EqualTo(1), $"round {round}");
            string soldOut = "08" + Int(17) + Long(listing.Id) + Int(0);
            List<string> loser = paid[0] == 0
                ? Sent()
                : otherTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.MesoMarket).Select(packet => Convert.ToHexString(packet)[4..]).ToList();
            Assert.That(loser, Is.EqualTo(new[] { soldOut }), $"round {round}");
            Assert.That((paid[0] == 0 ? first : other).Player.Value.Account.MesoMarketPurchased, Is.EqualTo(0), $"round {round}");
            Assert.That(test.Errors.Concat(otherTest.Errors), Is.Empty, $"round {round}");
        }
    }

    // A buyer and the owner's take-back at once: the mesos go to the buyer's mail or back to the owner, never
    // both, and the buyer pays only when it got them.
    [Test]
    public void Buy_RacingTheTakeBack() {
        for (int round = 0; round < 40; round++) {
            GameSession owner = Login();
            (TestGameSession buyerTest, GameSession buyer, MesoMarketHandler buyerHandler) = Second(500);
            MesoListing listing;
            using (GameStorage.Request create = test.Storage.Context()) {
                listing = create.CreateMesoListing(new MesoListing(TimeSpan.FromDays(2)) {
                    AccountId = owner.AccountId,
                    CharacterId = owner.CharacterId,
                    Amount = BASE,
                    Price = 200,
                })!;
            }

            Parallel.Invoke(
                () => Buy(buyerHandler, buyer, listing.Id),
                () => Send(owner, 6, listing.Id));

            long paid = 500 - buyer.Currency[CurrencyType.MesoToken];
            Assert.That(paid, Is.EqualTo(0).Or.EqualTo(200), $"round {round}");
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.GetMesoListing(listing.Id), Is.Null, $"round {round}");
            Assert.That(db.GetAllMail(buyer.CharacterId).Sum(mail => mail.Meso), Is.EqualTo(paid == 0 ? 0 : BASE), $"round {round}");
            Assert.That(owner.Currency.Meso, Is.EqualTo(paid == 0 ? BASE : 0), $"round {round}");
            Assert.That(db.GetAllMail(owner.CharacterId).Sum(mail => mail.Meret), Is.EqualTo(paid == 0 ? 0 : 190), $"round {round}");
            Assert.That(test.Errors.Concat(buyerTest.Errors), Is.Empty, $"round {round}");
        }
    }
}

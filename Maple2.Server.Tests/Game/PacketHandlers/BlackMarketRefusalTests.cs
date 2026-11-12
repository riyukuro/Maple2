using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The black market's money paths: every refusal leaves the mesos, the item and the listing as they were, and of
// two requests racing on one listing only what the listing holds is handed out.
public class BlackMarketRefusalTests {
    private const int ItemId = 20000005;
    private const long Price = 300;

    private TestGameSession test = null!;
    private readonly ItemMetadata metadata = TestGameSession.Metadata(ItemId);

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static GameSession Login(TestGameSession test, long meso, long minPrice = 1) {
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            BlackMarketSellMinPrice = minPrice,
            BlackMarketSellMaxPrice = 1000000,
            BlackMarketSellEndDay = 2,
        });
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        test.TakeSent();
        return session;
    }

    private static void Send(TestGameSession test, GameSession session, byte command, long id, params long[] values) {
        new BlackMarketHandler { TableMetadata = test.TableMetadata, World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(id);
            if (command == 2) {
                writer.WriteLong(values[0]);
                writer.WriteInt((int) values[1]);
            } else if (command == 5) {
                writer.WriteInt((int) values[0]);
            }
        }));
    }

    // A listing of amount items; by another account unless a seller is given.
    private BlackMarketListing List(int amount, TestGameSession? seller = null, int days = 1) {
        test.Register(metadata);
        // An item row names its owner by id alone. The server's tables number their rows in ranges of their own;
        // the test store numbers every table from 1, so the listing and mail ids are moved clear of the
        // characters' and of each other.
        using (var context = new Ms2Context(test.Options)) {
            var rows = Enumerable.Range(0, 30).Select(_ => new Maple2.Database.Model.BlackMarketListing()).ToArray();
            context.BlackMarketListing.AddRange(rows);
            context.SaveChanges();
            context.BlackMarketListing.RemoveRange(rows);
            context.SaveChanges();
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            for (int i = 0; i < 60; i++) {
                db.CreateMail(new Mail { ReceiverId = 999999 });
            }
        }
        Item item;
        using (GameStorage.Request db = test.Storage.Context()) {
            item = db.CreateItem(0, new Item(metadata, amount: amount)) ?? throw new InvalidOperationException("item row not created");
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            return db.CreateBlackMarketingListing(new BlackMarketListing(item) {
                AccountId = seller?.AccountId ?? test.AccountId + 100,
                CharacterId = seller?.CharacterId ?? test.CharacterId + 100,
                Deposit = 10,
                ExpiryTime = DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds(),
                Price = Price,
                Quantity = amount,
            }) ?? throw new InvalidOperationException("listing row not created");
        }
    }

    // Every row of the item: (owner, amount).
    private List<(long Owner, int Amount)> ItemRows() {
        using var context = new Ms2Context(test.Options);
        return context.Item.Where(row => row.ItemId == ItemId).AsEnumerable().Select(row => (row.OwnerId, row.Amount)).ToList();
    }

    private int MailedAmount(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(characterId).SelectMany(mail => mail.Items).Where(item => item.Id == ItemId).Sum(item => item.Amount);
    }

    // A buy refused: the one error answer; no mesos paid, no mail, the listing and its item as they were, and the
    // world (the seller's notice) is not told.
    [TestCase("gone", 4, 5000L, BlackMarketError.s_err_lack_itemcount)]
    [TestCase("expired", 4, 5000L, BlackMarketError.s_blackmarket_error_buy_expired)]
    [TestCase("own", 4, 5000L, BlackMarketError.s_blackmarket_error_cannot_buy_ownProduct)]
    [TestCase("", 0, 5000L, BlackMarketError.s_blackmarket_error_purchase_count)]
    [TestCase("", -1, 5000L, BlackMarketError.s_blackmarket_error_purchase_count)]
    [TestCase("", 5, 5000L, BlackMarketError.s_blackmarket_error_purchase_count)]
    [TestCase("", 4, 1199L, BlackMarketError.s_err_lack_meso)]
    public void Purchase_Refused(string kind, int count, long meso, BlackMarketError error) {
        test = new TestGameSession();
        BlackMarketListing listing = List(4, kind == "own" ? test : null, kind == "expired" ? -1 : 1);
        long listingId = kind == "gone" ? listing.Id + 50 : listing.Id;
        GameSession session = Login(test, meso);

        Send(test, session, 5, listingId, count);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Error(error, 5, listingId)) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        Assert.That(ItemRows(), Is.EqualTo(new[] { (listing.Id, 4) }));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListing(listing.Id)?.Quantity, Is.EqualTo(4));
        Assert.That(db.GetAllMail(session.CharacterId), Is.Empty);
        Assert.That(test.World.BlackMarketRequests, Is.Empty);
        Assert.That(test.World.MailNotifications, Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // A listing refused: the one error answer; no deposit paid, the stack whole in the bag, nothing listed.
    [TestCase(false, 1000L, 2, BlackMarketError.s_blackmarket_error_register_not_exist_in_inven)]
    [TestCase(true, 1000L, 6, BlackMarketError.s_blackmarket_error_lack_sale_count)]
    [TestCase(true, 1000L, 0, BlackMarketError.s_blackmarket_error_invalid_sale_count)]
    [TestCase(true, 1000L, -2, BlackMarketError.s_blackmarket_error_invalid_sale_count)]
    [TestCase(true, 0L, 2, BlackMarketError.s_blackmarket_error_fail_register)]
    [TestCase(true, -5L, 2, BlackMarketError.s_blackmarket_error_fail_register)]
    [TestCase(true, 1000001L, 2, BlackMarketError.s_blackmarket_error_fail_register)]
    [TestCase(true, 600000L, 2, BlackMarketError.s_blackmarket_error_fail_register)] // the total is over the maximum
    public void Add_Refused(bool own, long price, int count, BlackMarketError error) {
        test = new TestGameSession();
        Item item = test.Store(metadata, amount: 5);
        GameSession session = Login(test, 100000);

        Send(test, session, 2, own ? item.Uid : item.Uid + 50, price, count);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Error(error, 2)) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(100000));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(5));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListings(test.CharacterId), Is.Empty);
        Assert.That(test.World.BlackMarketRequests, Is.Empty);
    }

    // The played client's minimum is 100 for the total: 4 at 50 each (200) is listed, as the client sends it.
    [Test]
    public void Add_TheMinimumIsForTheTotal() {
        test = new TestGameSession();
        Item item = test.Store(metadata, amount: 5);
        GameSession session = Login(test, 100000, minPrice: 100);

        Send(test, session, 2, item.Uid, 50, 4);

        using GameStorage.Request db = test.Storage.Context();
        BlackMarketListing listing = db.GetBlackMarketListings(test.CharacterId).Single();
        Assert.That((listing.Price, listing.Quantity), Is.EqualTo((50L, 4)));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(1));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Not.Contain(Hex(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_fail_register, 2))));
    }

    // A take-back refused (another character's listing, or none): the error; the listing stays, nothing is mailed.
    [TestCase(false)]
    [TestCase(true)]
    public void Remove_Refused(bool gone) {
        test = new TestGameSession();
        BlackMarketListing listing = List(4);
        GameSession session = Login(test, 0);

        Send(test, session, 3, gone ? listing.Id + 50 : listing.Id);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close, 3)) }));
        Assert.That(ItemRows(), Is.EqualTo(new[] { (listing.Id, 4) }));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListing(listing.Id), Is.Not.Null);
        Assert.That(db.GetAllMail(session.CharacterId), Is.Empty);
        Assert.That(test.World.BlackMarketRequests, Is.Empty);
    }

    // A part bought, then the rest taken back: the buyer's mail holds the part, the seller's the rest, and the
    // deposit of a listing taken back before its end is not returned.
    [Test]
    public void Remove_AfterAPartWasBoughtReturnsTheRest() {
        test = new TestGameSession();
        var buyerTest = new TestGameSession(test.Options);
        buyerTest.Register(metadata);
        BlackMarketListing listing = List(4, test);
        GameSession seller = Login(test, 0);
        GameSession buyer = Login(buyerTest, 5000);

        Send(buyerTest, buyer, 5, listing.Id, 1);
        Send(test, seller, 3, listing.Id);

        Assert.That(buyer.Currency.Meso, Is.EqualTo(5000 - Price));
        Assert.That(MailedAmount(buyer.CharacterId), Is.EqualTo(1));
        Assert.That(MailedAmount(seller.CharacterId), Is.EqualTo(3));
        Assert.That(ItemRows().Sum(row => row.Amount), Is.EqualTo(4));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListing(listing.Id), Is.Null);
        Assert.That(db.GetAllMail(seller.CharacterId).Sum(mail => mail.Meso), Is.EqualTo(Price - (long) (Price * Maple2.Server.Game.LuaFunctions.Lua.CalcBlackMarketCostRate())));
        Assert.That(test.Errors.Concat(buyerTest.Errors), Is.Empty);
    }

    // A row with nothing left is what another buyer's transaction shows between its two saves on a database
    // without transactions: it is a sold listing, so the answer is the one for no row.
    [Test]
    public void Purchase_OfARowWithNothingLeftIsGone() {
        test = new TestGameSession();
        BlackMarketListing listing = List(4);
        using (var context = new Ms2Context(test.Options)) {
            context.BlackMarketListing.Find(listing.Id)!.Quantity = 0;
            context.SaveChanges();
        }
        GameSession session = Login(test, 5000);

        Send(test, session, 5, listing.Id, 4);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Error(BlackMarketError.s_err_lack_itemcount, 5, listing.Id)) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(0));
    }

    // Two buyers at once on one listing, each asking for count of its 4: with 4 one gets it all and the other is
    // refused; with 3 one gets 3 and 1 stays listed; with 2 both are served. Only served buyers pay, and the item
    // rows always add up to the 4 that were listed.
    [TestCase(4, 1)]
    [TestCase(3, 1)]
    [TestCase(2, 2)]
    public void Purchase_TwoBuyersRacing(int count, int served) {
        for (int round = 0; round < 40; round++) {
            test = new TestGameSession();
            BlackMarketListing listing = List(4);
            var tests = new[] { test, new TestGameSession(test.Options) };
            tests[1].Register(metadata);
            GameSession[] buyers = tests.Select(buyer => Login(buyer, 5000)).ToArray();

            Parallel.For(0, 2, i => Send(tests[i], buyers[i], 5, listing.Id, count));

            int[] paid = buyers.Select(buyer => (int) ((5000 - buyer.Currency.Meso) / Price)).ToArray();
            int[] got = buyers.Select(buyer => MailedAmount(buyer.CharacterId)).ToArray();
            Assert.That(got, Is.EqualTo(paid), $"round {round}");
            Assert.That(paid.Count(amount => amount == count), Is.EqualTo(served), $"round {round}");
            Assert.That(paid.Sum(), Is.EqualTo(served * count), $"round {round}");
            List<(long Owner, int Amount)> rows = ItemRows();
            Assert.That(rows.Sum(row => row.Amount), Is.EqualTo(4), $"round {round}");
            using GameStorage.Request db = test.Storage.Context();
            int left = 4 - served * count;
            Assert.That(db.GetBlackMarketListing(listing.Id)?.Quantity ?? 0, Is.EqualTo(left), $"round {round}");
            Assert.That(rows.Where(row => row.Owner == listing.Id).Sum(row => row.Amount), Is.EqualTo(left), $"round {round}");
            for (int i = 0; i < 2; i++) {
                List<byte[]> sent = tests[i].TakeSent();
                if (paid[i] == 0) {
                    BlackMarketError error = left == 0 ? BlackMarketError.s_err_lack_itemcount : BlackMarketError.s_blackmarket_error_purchase_count;
                    Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Error(error, 5, listing.Id)) }), $"round {round}");
                } else {
                    Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Meso, SendOp.BlackMarket, SendOp.Mail }), $"round {round}");
                }
            }
            Assert.That(tests.SelectMany(buyer => buyer.Errors), Is.Empty, $"round {round}");
        }
    }

    // A buyer and the seller's take-back at once: the buyer gets its 3 and the seller the 1 left, or the seller
    // gets all 4 back and the buyer pays nothing. The items add up to 4 either way.
    [Test]
    public void Purchase_RacingTheTakeBack() {
        for (int round = 0; round < 40; round++) {
            test = new TestGameSession();
            var buyerTest = new TestGameSession(test.Options);
            buyerTest.Register(metadata);
            BlackMarketListing listing = List(4, test);
            GameSession seller = Login(test, 0);
            GameSession buyer = Login(buyerTest, 5000);

            Parallel.Invoke(
                () => Send(buyerTest, buyer, 5, listing.Id, 3),
                () => Send(test, seller, 3, listing.Id));

            int paid = (int) ((5000 - buyer.Currency.Meso) / Price);
            Assert.That(paid, Is.EqualTo(0).Or.EqualTo(3), $"round {round}");
            Assert.That(MailedAmount(buyer.CharacterId), Is.EqualTo(paid), $"round {round}");
            Assert.That(MailedAmount(seller.CharacterId), Is.EqualTo(4 - paid), $"round {round}");
            Assert.That(ItemRows().Sum(row => row.Amount), Is.EqualTo(4), $"round {round}");
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.GetBlackMarketListing(listing.Id), Is.Null, $"round {round}");
            Assert.That(test.Errors.Concat(buyerTest.Errors), Is.Empty, $"round {round}");
        }
    }
}

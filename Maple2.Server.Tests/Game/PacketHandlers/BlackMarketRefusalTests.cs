using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

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
    private BlackMarketListing List(int amount, TestGameSession? seller = null, int days = 1, ItemSocket? socket = null) {
        test.Register(metadata);
        MoveIdsClear();
        Item item;
        using (GameStorage.Request db = test.Storage.Context()) {
            item = db.CreateItem(0, new Item(metadata, amount: amount) { Socket = socket }) ?? throw new InvalidOperationException("item row not created");
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

    // An item row names its owner by id alone. The server's tables number their rows in ranges of their own; the
    // test store numbers every table from 1, so the listing and mail ids are moved clear of the characters' and of
    // each other.
    private void MoveIdsClear() {
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

    // An item the market does not take (CanListOnBlackMarket): locked, bound, of no trade, of a limited trade with
    // no trades left, a written music score. The generic refusal; no deposit, the stack in the bag, nothing listed.
    // A tradeable one and one of a limited trade with a trade left are listed.
    [TestCase("locked", true)]
    [TestCase("bound", true)]
    [TestCase("untradeable", true)]
    [TestCase("noTradesLeft", true)]
    [TestCase("writtenScore", true)]
    [TestCase("tradeable", false)]
    [TestCase("tradesLeft", false)]
    public void Add_ItemTheMarketDoesNotTake(string kind, bool refused) {
        test = new TestGameSession();
        Item stored = test.Store(metadata, amount: 5);
        GameSession session = Login(test, 100000);
        Item item = session.Item.Inventory.Get(stored.Uid)!;
        switch (kind) {
            case "locked":
                item.IsLocked = true;
                break;
            case "bound":
                item.Transfer = new ItemTransfer(TransferFlag.Trade, binding: new ItemBinding(test.CharacterId, "bound"));
                break;
            case "untradeable":
                item.Transfer = new ItemTransfer(TransferFlag.Split);
                break;
            case "noTradesLeft":
                item.Transfer = new ItemTransfer(TransferFlag.LimitTrade, remainTrades: 0);
                break;
            case "writtenScore":
                item.Music = new ItemCustomMusicScore { Author = "writer", Title = "song" };
                break;
            case "tradeable":
                item.Transfer = new ItemTransfer(TransferFlag.Trade);
                break;
            case "tradesLeft":
                item.Transfer = new ItemTransfer(TransferFlag.LimitTrade, remainTrades: 1);
                break;
        }

        Send(test, session, 2, item.Uid, 1000, 2);

        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        string refusal = Hex(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_disable_registitem, 2));
        using GameStorage.Request db = test.Storage.Context();
        if (refused) {
            Assert.That(sent, Is.EqualTo(new[] { refusal }));
            Assert.That(session.Currency.Meso, Is.EqualTo(100000));
            Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(5));
            Assert.That(db.GetBlackMarketListings(test.CharacterId), Is.Empty);
            Assert.That(test.World.BlackMarketRequests, Is.Empty);
        } else {
            Assert.That(sent, Does.Not.Contain(refusal));
            Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(3));
            Assert.That(db.GetBlackMarketListings(test.CharacterId).Count(), Is.EqualTo(1));
        }
    }

    // A deposit the wallet cannot pay: s_err_lack_meso; the mesos, the stack and the listings as they were. One meso
    // more and it is listed.
    [TestCase(-1, true)]
    [TestCase(0, false)]
    public void Add_TheDepositIsCheckedServerSide(long off, bool refused) {
        test = new TestGameSession();
        Item item = test.Store(metadata, amount: 5);
        long deposit = Maple2.Server.Game.LuaFunctions.Lua.CalcBlackMarketRegisterDeposit(2000);
        GameSession session = Login(test, deposit + off);

        Send(test, session, 2, item.Uid, 1000, 2);

        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        string lack = Hex(BlackMarketPacket.Error(BlackMarketError.s_err_lack_meso, 2));
        using GameStorage.Request db = test.Storage.Context();
        if (refused) {
            Assert.That(sent, Is.EqualTo(new[] { lack }));
            Assert.That(session.Currency.Meso, Is.EqualTo(deposit + off));
            Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(5));
            Assert.That(db.GetBlackMarketListings(test.CharacterId), Is.Empty);
        } else {
            Assert.That(sent, Does.Not.Contain(lack));
            Assert.That(session.Currency.Meso, Is.EqualTo(0));
            Assert.That(db.GetBlackMarketListings(test.CharacterId).Single().Deposit, Is.EqualTo(deposit));
        }
    }

    // Mode 1: the character's own listings, and no other's, in the one mode 1 answer.
    [Test]
    public void MyListings_OnlyTheCharactersOwn() {
        test = new TestGameSession();
        BlackMarketListing own = List(2, test);
        List(3);
        GameSession session = Login(test, 0);

        new BlackMarketHandler { TableMetadata = test.TableMetadata, World = test.World }
            .Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));

        List<byte[]> sent = test.TakeSent().ToList();
        Assert.That(sent, Has.Count.EqualTo(1));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Hex(BlackMarketPacket.MyListings(db.GetBlackMarketListings(test.CharacterId).ToList()))));
        Assert.That(sent[0][3..7], Is.EqualTo(BitConverter.GetBytes(1)));
        Assert.That(db.GetBlackMarketListings(test.CharacterId).Single().Id, Is.EqualTo(own.Id));
    }

    // Mode 4 as CBlackMarketSchCond::WriteStream writes it, with KR blackmarkettable rows: the tabs in the id
    // range give the categories asked of World (31130 and 31131, not 32122), a stat line with no value is left out,
    // and the listings World finds are answered in its order. A refusal by World is the mode 4 error.
    [TestCase(0)]
    [TestCase((int) BlackMarketError.s_err_null_product)]
    public void Search_AsksWorldAndListsItsFinds(int answer) {
        test = new TestGameSession();
        test.SetTable(new BlackMarketTable(new Dictionary<int, string[]> {
            [31130] = ["130_equip"],
            [31131] = ["131_equip"],
            [32122] = ["122_equip"],
        }));
        BlackMarketListing first = List(1);
        BlackMarketListing second = List(2);
        GameSession session = Login(test, 0);
        test.World.BlackMarketReply = _ => answer != 0
            ? new BlackMarketResponse { Error = answer }
            : new BlackMarketResponse { Search = new BlackMarketResponse.Types.Search { ListingIds = { second.Id, first.Id } } };

        new BlackMarketHandler { TableMetadata = test.TableMetadata, World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(31130); // tab id range
            writer.WriteInt(31131);
            writer.WriteInt(50); // level range
            writer.WriteInt(60);
            writer.WriteInt(0); // job flag
            writer.WriteInt(4); // rarity
            writer.WriteInt(0); // enchant range
            writer.WriteInt(15);
            writer.WriteByte(0); // socket range
            writer.WriteByte(3);
            writer.WriteUnicodeString("");
            writer.WriteInt(1); // page
            writer.WriteByte(1); // sort
            writer.WriteByte(0);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteByte(0);
            writer.WriteByte(1);
            for (int i = 0; i < 3; i++) {
                writer.WriteInt(0);
                writer.WriteInt(0);
            }
        }));

        BlackMarketRequest.Types.Search search = test.World.BlackMarketRequests.Single().Search;
        Assert.That(search.Categories, Is.EqualTo(new[] { "130_equip", "131_equip" }));
        Assert.That((search.MinLevel, search.MaxLevel, search.Rarity, search.MaxEnchantLevel, search.MaxSocketCount, search.StartPage),
            Is.EqualTo((50, 60, 4, 15, 3, 1)));
        Assert.That(search.StatOptions, Is.Empty);
        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        if (answer != 0) {
            Assert.That(sent, Is.EqualTo(new[] { Hex(BlackMarketPacket.Error((BlackMarketError) answer, 4)) }));
        } else {
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0][6..14], Is.EqualTo(Convert.ToHexString(BitConverter.GetBytes(2))));
        }
    }

    // Mode 8 (CBlackMarketMan::SendBlackMarketMode8): the item id and rarity come back with the item's npc sell
    // price, read by FUN_140a21be0 as int, int, long. An unknown item id is not answered.
    [Test]
    public void Preview_TheSellPrice() {
        test = new TestGameSession();
        test.Register(metadata);
        GameSession session = Login(test, 0);

        foreach (int id in new[] { ItemId, 1 }) {
            new BlackMarketHandler { TableMetadata = test.TableMetadata, World = test.World }.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(8);
                writer.WriteInt(id);
                writer.WriteInt(3);
            }));
        }

        long price = Maple2.Server.Core.Formulas.Shop.SellPrice(metadata, 3, test.ServerTableMetadata.ConstantsTable);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Preview(ItemId, 3, price)) }));
    }

    // An item past its time: the refusal is answered (it used to be dropped without a word, the client left
    // waiting); no deposit, the stack in the bag.
    [Test]
    public void Add_ExpiredItem_Refused() {
        test = new TestGameSession();
        Item item = test.Store(metadata, amount: 5);
        item.ExpiryTime = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        using (GameStorage.Request save = test.Storage.Context()) {
            Assert.That(save.SaveItems(test.CharacterId, item), Is.True);
        }
        GameSession session = Login(test, 100000);

        Send(test, session, 2, item.Uid, 1000, 2);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_disable_registitem, 2)) }));
        Assert.That(session.Currency.Meso, Is.EqualTo(100000));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(5));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListings(test.CharacterId), Is.Empty);
    }

    // The client lists at most 28 at once (box 0x933 with 28): with 28 listed, sold out or expired ones too, a
    // 29th is refused with error 21 and nothing is taken; with 27 it is listed.
    [TestCase(27, false)]
    [TestCase(28, true)]
    public void Add_TheListingCap(int listed, bool refused) {
        test = new TestGameSession();
        for (int i = 0; i < listed; i++) {
            List(1, test, days: i % 2 == 0 ? 1 : -1);
        }
        Item item = test.Store(metadata, amount: 5);
        GameSession session = Login(test, 100000);

        Send(test, session, 2, item.Uid, 1000, 2);

        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        string capError = Hex(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_max_register_item, 2));
        using GameStorage.Request db = test.Storage.Context();
        if (refused) {
            Assert.That(sent, Is.EqualTo(new[] { capError }));
            Assert.That(session.Currency.Meso, Is.EqualTo(100000));
            Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(5));
            Assert.That(db.GetBlackMarketListings(test.CharacterId).Count(), Is.EqualTo(listed));
        } else {
            Assert.That(sent, Does.Not.Contain(capError));
            Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(3));
            Assert.That(db.GetBlackMarketListings(test.CharacterId).Count(), Is.EqualTo(listed + 1));
        }
        Assert.That(test.Errors, Is.Empty);
    }

    // The deposit is the client's: CalcBlackMarketRegisterDeposit of the total, 1 of 100 rounded up. The script's
    // deposit percent is 1, so the old form (total times the percent first) gives the same 31: this checks the
    // amount and cannot catch the old form while the script stays as it is.
    [Test]
    public void Add_TakesTheClientsDeposit() {
        test = new TestGameSession();
        Item item = test.Store(metadata, amount: 5);
        GameSession session = Login(test, 100000);

        Send(test, session, 2, item.Uid, 1001, 3);

        Assert.That(100000 - session.Currency.Meso, Is.EqualTo(Maple2.Server.Game.LuaFunctions.Lua.CalcBlackMarketRegisterDeposit(3003)));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListings(test.CharacterId).Single().Deposit, Is.EqualTo(100000 - session.Currency.Meso));
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

    // World refuses the new listing: the error, the row is taken back, and the item and the deposit are mailed to
    // the seller (the row used to stay, and World loaded it at its next start, to be bought with the refunded item).
    [Test]
    public void Add_RefusedByWorld_LeavesNoRow() {
        test = new TestGameSession();
        test.World.BlackMarketAnswer = (int) BlackMarketError.s_blackmarket_error_fail_register;
        MoveIdsClear();
        Item item = test.Store(metadata, amount: 5);
        GameSession session = Login(test, 100000);

        Send(test, session, 2, item.Uid, 1000, 2);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Does.Contain(Hex(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_fail_register, 2))));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListings(test.CharacterId), Is.Empty, string.Join("\n", test.Errors));
        Mail mail = db.GetAllMail(session.CharacterId).Single();
        // The deposit taken from the purse comes back in the mail.
        Assert.That(session.Currency.Meso, Is.LessThan(100000));
        Assert.That((mail.Type, mail.Meso), Is.EqualTo((MailType.BlackMarketFail, 100000 - session.Currency.Meso)));
        Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(2));
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(3));
        Assert.That(test.Errors, Is.Empty);
    }

    // World refuses the new listing and the row cannot be taken back: the row and its item stay listed (the seller
    // can take them back), and the deposit is mailed now, with the row's deposit set to 0 so a cancel after expiry
    // does not pay it again (the deposit used to be lost: a cancel before expiry refunds none).
    [Test]
    public void Add_RefusedByWorld_TakeFails_DepositRefunded() {
        test = new TestGameSession();
        test.World.BlackMarketAnswer = (int) BlackMarketError.s_blackmarket_error_fail_register;
        MoveIdsClear();
        Item item = test.Store(metadata, amount: 5);
        GameSession session = Login(test, 100000);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.BlackMarketListing>()
            .Any(entry => entry.State == Microsoft.EntityFrameworkCore.EntityState.Deleted);

        Send(test, session, 2, item.Uid, 1000, 2);
        test.FailSave = null;

        long deposit = 100000 - session.Currency.Meso;
        Assert.That(deposit, Is.GreaterThan(0));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListings(test.CharacterId).Count(), Is.EqualTo(1));
        Mail mail = db.GetAllMail(session.CharacterId).Single();
        Assert.That((mail.Type, mail.Meso), Is.EqualTo((MailType.BlackMarketFail, deposit)));
        Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(0));
        using var context = new Ms2Context(test.Options);
        Assert.That(context.BlackMarketListing.Select(row => row.Deposit).ToList(), Is.EqualTo(new[] { 0L }));
    }

    // A take-back refused: another character's listing gets the error; one that is gone (sold out, or taken back
    // already) is answered as removed already, so the client drops it from its list. The listing that is there
    // stays, nothing is mailed.
    [TestCase(false)]
    [TestCase(true)]
    public void Remove_Refused(bool gone) {
        test = new TestGameSession();
        BlackMarketListing listing = List(4);
        GameSession session = Login(test, 0);

        long listingId = gone ? listing.Id + 50 : listing.Id;
        Send(test, session, 3, listingId);

        string answer = gone ? Hex(BlackMarketPacket.Remove(listingId, already: true)) : Hex(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_close, 3));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { answer }));
        Assert.That(ItemRows(), Is.EqualTo(new[] { (listing.Id, 4) }));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetBlackMarketListing(listing.Id), Is.Not.Null);
        Assert.That(db.GetAllMail(session.CharacterId), Is.Empty);
        Assert.That(test.World.BlackMarketRequests, Is.Empty);
    }

    // The search's socket bounds count an item without sockets as 0 sockets: a minimum of 1 leaves it out, as it
    // leaves out an item with its sockets all locked, and a maximum keeps it.
    [Test]
    public void Search_SocketBounds_CountNoSocketsAsZero() {
        test = new TestGameSession();
        BlackMarketListing none = List(1);
        BlackMarketListing locked = List(1, socket: new ItemSocket(3, 0));
        BlackMarketListing one = List(1, socket: new ItemSocket(3, 1));
        using var lookup = new Maple2.Server.World.Containers.BlackMarketLookup(test.Storage);
        string[] categories = { metadata.Property.BlackMarketCategory };

        ICollection<long> Find(int minSockets, int maxSockets) => lookup.Search(categories, 0, 0, JobFilterFlag.None, 0, 0, 0,
            minSockets, maxSockets, "", 1, BlackMarketSort.PriceAscending, new Dictionary<BasicAttribute, BasicOption>(), new Dictionary<SpecialAttribute, SpecialOption>());

        Assert.Multiple(() => {
            Assert.That(Find(1, 3), Is.EquivalentTo(new[] { one.Id }));
            Assert.That(Find(0, 3), Is.EquivalentTo(new[] { none.Id, locked.Id, one.Id }));
            Assert.That(Find(0, 0), Is.EquivalentTo(new[] { none.Id, locked.Id, one.Id }));
        });
    }

    // The take-back sent again after it went through (a second click, or two sessions): the item is mailed
    // once, and the second answer is the removed-already one (flag 1), not an error.
    [Test]
    public void Remove_Twice_MailsOnce() {
        test = new TestGameSession();
        BlackMarketListing listing = List(4, test);
        GameSession session = Login(test, 0);

        Send(test, session, 3, listing.Id);
        test.TakeSent();
        Send(test, session, 3, listing.Id);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(BlackMarketPacket.Remove(listing.Id, already: true)) }));
        Assert.That(sent[0][^1], Is.EqualTo(1));
        Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(4));
        Assert.That(ItemRows().Sum(row => row.Amount), Is.EqualTo(4));
        Assert.That(test.Errors, Is.Empty);
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
                    // Only the refusal, at the look or by the transaction: the price held without a packet goes back
                    // without one, so the balance the client shows never changed.
                    Assert.That(sent.SkipLast(1).Select(TestGameSession.Op), Is.Empty, $"round {round}");
                    Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(BlackMarketPacket.Error(error, 5, listing.Id))), $"round {round}");
                } else {
                    Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Meso, SendOp.BlackMarket, SendOp.Mail }), $"round {round}");
                }
            }
            Assert.That(tests.SelectMany(buyer => buyer.Errors), Is.Empty, $"round {round}");
        }
    }

    // The buyer spends mesos while the buy runs (here at the seller's look-up, between the check and the database
    // transaction): the price was taken first, so only what was left could be spent. Before, the price was taken
    // after the item was mailed; that take threw on the short balance, and the item was free.
    [Test]
    public void Purchase_PaysBeforeTheItemIsMailed() {
        test = new TestGameSession();
        BlackMarketListing listing = List(4);
        GameSession session = Login(test, 5000);
        long spent = 0;
        test.World.OnPlayerInfo = _ => {
            long left = session.Currency.Meso;
            if (session.Currency.TryTakeMeso(left)) {
                spent += left;
            }
        };

        Assert.DoesNotThrow(() => Send(test, session, 5, listing.Id, 1));

        Assert.Multiple(() => {
            Assert.That(spent, Is.EqualTo(5000 - Price));
            Assert.That(session.Currency.Meso, Is.EqualTo(0));
            Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(1));
        });
    }

    // A throw after the buy is stored (while its answer is shaped): the item is mailed, so the price stays taken.
    // Before, the refund on a throw also ran here, and the buyer had the item and the mesos back.
    [Test]
    public void Purchase_AThrowAfterTheBuyIsStoredKeepsThePrice() {
        test = new TestGameSession();
        BlackMarketListing listing = List(4);
        GameSession session = Login(test, 5000);
        Maple2.Database.Storage.GameStorage.Request.OnBuyStored = id => {
            if (id == listing.Id) {
                throw new InvalidOperationException("after the commit");
            }
        };
        try {
            Assert.Throws<InvalidOperationException>(() => Send(test, session, 5, listing.Id, 1));
        } finally {
            Maple2.Database.Storage.GameStorage.Request.OnBuyStored = null;
        }

        Assert.Multiple(() => {
            Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(1));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000 - Price));
        });
    }

    // The transaction refuses the buy (here the row is sold out at the seller's look-up). Without a meso packet
    // meanwhile the price goes back silently: the client still shows the balance before the take. With one
    // meanwhile (here 1 meso added) the client shows the held balance, so the give-back sends the right one.
    [TestCase(false)]
    [TestCase(true)]
    public void Purchase_RefusedByTheTransaction_ShowsTheBalanceBack(bool mesoPacketMeanwhile) {
        test = new TestGameSession();
        BlackMarketListing listing = List(4);
        GameSession session = Login(test, 5000);
        test.World.OnPlayerInfo = _ => {
            using (var context = new Ms2Context(test.Options)) {
                context.BlackMarketListing.Find(listing.Id)!.Quantity = 0;
                context.SaveChanges();
            }
            if (mesoPacketMeanwhile) {
                session.Currency.AddMeso(1, 0);
            }
        };

        Send(test, session, 5, listing.Id, 1);

        long meso = mesoPacketMeanwhile ? 5001 : 5000;
        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(0));
            Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(mesoPacketMeanwhile
                ? new[] { SendOp.Meso, SendOp.Meso, SendOp.BlackMarket }
                : new[] { SendOp.BlackMarket }));
            if (mesoPacketMeanwhile) {
                Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Convert.ToHexString(CurrencyPacket.UpdateMeso(session.Player.Value.Currency).ToArray())));
            }
        });
    }

    // A throw before the commit (here at the seller's look-up): the price goes back without a packet, as none was
    // sent for the take.
    [Test]
    public void Purchase_AThrowBeforeTheCommitGivesThePriceBackSilently() {
        test = new TestGameSession();
        BlackMarketListing listing = List(4);
        GameSession session = Login(test, 5000);
        test.World.OnPlayerInfo = _ => throw new InvalidOperationException("before the commit");

        Assert.Throws<InvalidOperationException>(() => Send(test, session, 5, listing.Id, 1));

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(MailedAmount(session.CharacterId), Is.EqualTo(0));
            Assert.That(test.TakeSent(), Is.Empty);
        });
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

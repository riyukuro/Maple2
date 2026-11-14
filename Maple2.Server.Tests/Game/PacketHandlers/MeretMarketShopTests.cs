using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The design market's own-shop and bookmark requests, as the client sends them (meratmarketman.cpp):
// - 14 (OnAskRegisterSale, a listing not on sale) deletes the listing; 15 (OnAskCancelSale, a listing on sale,
//   state 2) stops it. Both write the product key (int, long) and the long uid.
// - 20 asks for one settled sale's profit by the seller-list id (CUgcMarketSeller::m_n18, the first long of the
//   sale entry), once the sale is UGCShopProfitDelayDays old (IsSettled).
// - 22 loads the bookmarks, 23 adds one by character id, 24 removes one, 25 asks for a bookmark's listings.
// The ids are made up.
public class MeretMarketShopTests {
    private const int Design = 11300002;
    private const int Town = 90000100;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private long CreateListing(UgcMarketListingStatus status, long endTime, long characterId = 0, long accountId = 0) {
        ItemMetadata metadata = TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1);
        test.Store(metadata);
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateUgcMarketItem(new UgcMarketItem(metadata) {
            Price = 500,
            TabId = 1,
            Status = status,
            ListingEndTime = endTime,
            PromotionEndTime = 0,
            SellerAccountId = accountId == 0 ? test.AccountId : accountId,
            SellerCharacterId = characterId == 0 ? test.CharacterId : characterId,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Id = 5001, Name = "Listed", Url = "item/ugc/listed.png" },
            Blueprint = new ItemBlueprint(),
        })!.Id;
    }

    private long CreateSale(long listingId, long profit, DateTime soldTime) {
        using GameStorage.Request db = test.Storage.Context();
        return db.AddUgcMarketSale(listingId, new SoldUgcMarketItem {
            AccountId = test.AccountId,
            Price = 500,
            Name = "Listed",
            Profit = profit,
            SoldTime = soldTime.ToEpochSeconds(),
        })!.Id;
    }

    private void Start() {
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meret = 1000;
        test.TakeSent();
    }

    private MeretMarketHandler Handler() => new() { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };

    private List<byte[]> Send(Action<IByteWriter> body) {
        Handler().Handle(session, TestGameSession.Packet(body));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
    }

    private List<byte[]> KeyRequest(byte mode, long id) => Send(writer => {
        writer.WriteByte(mode);
        writer.WriteInt(0);
        writer.WriteLong(id);
        writer.WriteLong(id);
    });

    private List<byte[]> IdRequest(byte mode, long id) => Send(writer => {
        writer.WriteByte(mode);
        writer.WriteLong(id);
    });

    private UgcMarketItem? Stored(long id) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetUgcMarketItem(id);
    }

    private bool SaleStored(long id) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMySoldUgcListings(test.AccountId).ContainsKey(id);
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    [Test]
    public void Delete_AListingNotOnSale() {
        long listing = CreateListing(UgcMarketListingStatus.Expired, 0);
        Start();
        List<byte[]> sent = KeyRequest(14, listing);
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(MeretMarketPacket.RemoveListing(listing)) }));
        Assert.That(Stored(listing), Is.Null);
        Assert.That(session.UgcMarket.TryGetItem(listing, out _), Is.False);
    }

    // The client sends 14 only for a listing not on sale; a listing on sale is stopped first (15).
    [Test]
    public void Delete_RefusedWhileOnSale() {
        long listing = CreateListing(UgcMarketListingStatus.Active, DateTime.Now.AddDays(3).ToEpochSeconds());
        Start();
        Assert.That(KeyRequest(14, listing), Is.Empty);
        Assert.That(Stored(listing), Is.Not.Null);
    }

    [Test]
    public void Delete_AnotherAccountsListingIsNotTouched() {
        long listing = CreateListing(UgcMarketListingStatus.Expired, 0, characterId: 9001, accountId: 9002);
        Start();
        Assert.That(KeyRequest(14, listing), Is.Empty);
        Assert.That(Stored(listing), Is.Not.Null);
    }

    [Test]
    public void Stop_AListingOnSale() {
        long listing = CreateListing(UgcMarketListingStatus.Active, DateTime.Now.AddDays(3).ToEpochSeconds());
        Start();
        List<byte[]> sent = KeyRequest(15, listing);
        Assert.That(sent, Has.Count.EqualTo(1));
        UgcMarketItem stored = Stored(listing)!;
        Assert.That((stored.Status, stored.ListingEndTime, stored.RelistKind),
            Is.EqualTo((UgcMarketListingStatus.Expired, 0L, UgcMarketItem.StoppedKind)));
        Assert.That(sent[0], Is.EqualTo(MeretMarketPacket.UpdateExpiration(stored).ToArray()));
    }

    [Test]
    public void Stop_RefusedWhenNotOnSale() {
        long listing = CreateListing(UgcMarketListingStatus.Expired, 0);
        Start();
        Assert.That(KeyRequest(15, listing), Is.Empty);
    }

    // A sale due for payment: the profit is paid in merets once, the sale leaves the stored list, and the answer is
    // 0x14 with result 1018 and the amount (OnSellerSettled), then the sales total (0x1a).
    [Test]
    public void CollectProfit_PaysADueSaleOnce() {
        long listing = CreateListing(UgcMarketListingStatus.Active, DateTime.Now.AddDays(3).ToEpochSeconds());
        long sale = CreateSale(listing, 350, DateTime.Now.AddDays(-Constant.UGCShopProfitDelayDays - 1));
        Start();
        List<byte[]> sent = IdRequest(20, sale);
        Assert.That(session.Currency.Meret, Is.EqualTo(1350));
        Assert.That(SaleStored(sale), Is.False);
        Assert.That(sent.Select(Convert.ToHexString).First(), Is.EqualTo(Hex(MeretMarketPacket.CollectProfit(sale, 350))));

        Assert.That(IdRequest(20, sale), Is.Empty);
        Assert.That(session.Currency.Meret, Is.EqualTo(1350));
    }

    [Test]
    public void CollectProfit_NotBeforeTheDelay() {
        long listing = CreateListing(UgcMarketListingStatus.Active, DateTime.Now.AddDays(3).ToEpochSeconds());
        long sale = CreateSale(listing, 350, DateTime.Now.AddDays(-Constant.UGCShopProfitDelayDays + 1));
        Start();
        Assert.That(IdRequest(20, sale), Is.Empty);
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        Assert.That(SaleStored(sale), Is.True);
    }

    [Test]
    public void CollectProfit_UnknownSaleIsNotPaid() {
        Start();
        Assert.That(IdRequest(20, 424242), Is.Empty);
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
    }

    [Test]
    public void Bookmark_AddRemoveAndLoad() {
        test.World.AddPlayer(9002, 9001, "Designer");
        long theirs = CreateListing(UgcMarketListingStatus.Active, DateTime.Now.AddDays(3).ToEpochSeconds(), characterId: 9001, accountId: 9002);
        Start();

        List<byte[]> added = IdRequest(23, 9001);
        Assert.That(added, Has.Count.EqualTo(1));
        var reader = new ByteReader(added[0], 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0x17));
        Assert.That((reader.ReadLong(), reader.ReadLong(), reader.ReadUnicodeString()), Is.EqualTo((9002L, 9001L, "Designer")));
        reader.ReadUnicodeString();
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(session.Config.GetFavoriteDesigners(), Is.EqualTo(new[] { 9001L }));
        Assert.That(theirs, Is.GreaterThan(0));

        // A second add of the same designer answers the listings again (0x19), not a second bookmark.
        List<byte[]> again = IdRequest(23, 9001);
        Assert.That(again.Select(p => p[2]), Is.EqualTo(new byte[] { 0x19 }));
        Assert.That(session.Config.GetFavoriteDesigners(), Has.Count.EqualTo(1));

        Assert.That(IdRequest(25, 9001).Select(p => p[2]), Is.EqualTo(new byte[] { 0x19 }));

        List<byte[]> loaded = Send(writer => writer.WriteByte(22));
        Assert.That(loaded.Select(p => p[2]), Is.EqualTo(new byte[] { 0x16 }));
        Assert.That(loaded[0][3], Is.EqualTo(1));

        Assert.That(IdRequest(24, 9001).Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(MeretMarketPacket.RemoveDesigner(9001)) }));
        Assert.That(session.Config.GetFavoriteDesigners(), Is.Empty);
        Assert.That(IdRequest(24, 9001), Is.Empty);
        Assert.That(IdRequest(25, 9001), Is.Empty);
    }

    [Test]
    public void Bookmark_UnknownCharacterIsNotAdded() {
        Start();
        Assert.That(IdRequest(23, 777), Is.Empty);
        Assert.That(session.Config.GetFavoriteDesigners(), Is.Empty);
    }

    // The client holds 16 bookmarks (AddBookMark) and sends the add unchecked; the 17th is not stored.
    [Test]
    public void Bookmark_SixteenAreTheLimit() {
        for (int i = 1; i <= UgcMarketManager.MaxDesigners + 1; i++) {
            test.World.AddPlayer(8000 + i, 9000 + i, "Designer" + i);
        }
        Start();
        for (int i = 1; i <= UgcMarketManager.MaxDesigners; i++) {
            Assert.That(IdRequest(23, 9000 + i), Has.Count.EqualTo(1));
        }
        Assert.That(IdRequest(23, 9000 + UgcMarketManager.MaxDesigners + 1), Is.Empty);
        Assert.That(session.Config.GetFavoriteDesigners(), Has.Count.EqualTo(UgcMarketManager.MaxDesigners));
        Assert.That(Send(writer => writer.WriteByte(22))[0][3], Is.EqualTo(UgcMarketManager.MaxDesigners));
    }
}

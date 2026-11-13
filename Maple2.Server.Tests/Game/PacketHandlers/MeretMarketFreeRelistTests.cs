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
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The free first relist of the design market. A listing whose time ran out is stored expired when the seller's list
// loads (MeretMarket 11), with kind 3 while its free relist is unused; the client then offers the free relist
// (OnClickRemoveSale tests state 3 and kind 3) and sends MeretMarket 19 with the listing id. The relist puts the
// listing on sale again for UGCShopSaleDay days, takes no merets and works once. An owner's stop (15) is kind 1,
// which the client relists for the fee only. The ids are made up.
public class MeretMarketFreeRelistTests {
    private const int Design = 11300002;
    private const int Town = 90000100;
    private const int SaleDays = 30;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long listing;

    private void Start(long endTime, bool freeRelistUsed = false) {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { UGCShopSaleDay = SaleDays });
        ItemMetadata metadata = TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1);
        test.Store(metadata);
        using (GameStorage.Request db = test.Storage.Context()) {
            listing = db.CreateUgcMarketItem(new UgcMarketItem(metadata) {
                Price = 500,
                TabId = 1,
                Status = UgcMarketListingStatus.Active,
                ListingEndTime = endTime,
                PromotionEndTime = 0,
                SellerAccountId = test.AccountId,
                SellerCharacterId = test.CharacterId,
                SellerCharacterName = "Seller",
                Description = string.Empty,
                Tags = [],
                Look = new UgcItemLook { Id = 5001, Name = "Listed", Url = "item/ugc/listed.png" },
                Blueprint = new ItemBlueprint(),
                FreeRelistUsed = freeRelistUsed,
            })!.Id;
        }
        if (freeRelistUsed) {
            // CreateUgcMarketItem adds the whole row; the flag is kept as made.
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.GetUgcMarketItem(listing)!.FreeRelistUsed, Is.True);
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meret = 5000;
        test.TakeSent();
    }

    private MeretMarketHandler Handler() => new() { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };

    private List<byte[]> Load() {
        Handler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(11)));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
    }

    private List<byte[]> FreeRelist(long id) {
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(19);
            writer.WriteLong(id);
        }));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
    }

    private void Stop(long id) {
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(15);
            writer.WriteInt(0);
            writer.WriteLong(id);
            writer.WriteLong(id);
        }));
    }

    private UgcMarketItem Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetUgcMarketItem(listing)!;
    }

    // CMeratMarketMan::OnProductSaleStateChanged (0x15): the product key (int, long), the long uid, the state byte,
    // the kind byte and the long end time.
    private static (byte State, byte Kind, long End) SaleState(byte[] packet) {
        var reader = new ByteReader(packet, 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0x15));
        reader.ReadInt();
        reader.ReadLong();
        reader.ReadLong();
        (byte, byte, long) result = (reader.ReadByte(), reader.ReadByte(), reader.ReadLong());
        Assert.That(reader.Available, Is.EqualTo(0));
        return result;
    }

    [Test]
    public void EndedListingLoadsExpiredWithTheFreeRelist() {
        Start(DateTime.Now.AddDays(-1).ToEpochSeconds());
        Load();
        Assert.That(session.UgcMarket.TryGetItem(listing, out UgcMarketItem? item), Is.True);
        UgcMarketItem stored = Stored();
        Assert.Multiple(() => {
            Assert.That(item!.Status, Is.EqualTo(UgcMarketListingStatus.Expired));
            Assert.That(item.RelistKind, Is.EqualTo(UgcMarketItem.FreeRelistKind));
            Assert.That(stored.Status, Is.EqualTo(UgcMarketListingStatus.Expired));
            Assert.That(stored.RelistKind, Is.EqualTo(UgcMarketItem.FreeRelistKind));
            Assert.That(stored.FreeRelistUsed, Is.False);
        });
    }

    [Test]
    public void RunningListingLoadsUnchanged() {
        Start(DateTime.Now.AddDays(3).ToEpochSeconds());
        Load();
        UgcMarketItem stored = Stored();
        Assert.That((stored.Status, stored.RelistKind), Is.EqualTo((UgcMarketListingStatus.Active, (byte) 0)));
    }

    [Test]
    public void FreeRelistPutsTheListingOnSaleWithoutMerets() {
        Start(DateTime.Now.AddDays(-1).ToEpochSeconds());
        Load();
        long before = DateTime.Now.AddDays(SaleDays).ToEpochSeconds();
        List<byte[]> sent = FreeRelist(listing);
        long after = DateTime.Now.AddDays(SaleDays).ToEpochSeconds();

        Assert.That(sent.Select(p => p[2]), Is.EqualTo(new byte[] { 0x13, 0x15 }));
        Assert.That(session.UgcMarket.TryGetItem(listing, out UgcMarketItem? item), Is.True);
        Assert.That(sent[0], Is.EqualTo(MeretMarketPacket.FreeRelist(item!).ToArray()));
        (byte state, byte kind, long end) = SaleState(sent[1]);
        UgcMarketItem stored = Stored();
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(5000));
            Assert.That((state, kind), Is.EqualTo(((byte) UgcMarketListingStatus.Active, (byte) 0)));
            Assert.That(end, Is.InRange(before, after));
            Assert.That(stored.Status, Is.EqualTo(UgcMarketListingStatus.Active));
            Assert.That(stored.ListingEndTime, Is.InRange(before, after));
            Assert.That(stored.RelistKind, Is.EqualTo(0));
            Assert.That(stored.FreeRelistUsed, Is.True);
            Assert.That(item!.FreeRelistUsed, Is.True);
        });
    }

    [Test]
    public void SecondFreeRelistIsRefused() {
        Start(DateTime.Now.AddDays(-1).ToEpochSeconds());
        Load();
        FreeRelist(listing);
        long end = Stored().ListingEndTime;
        List<byte[]> sent = FreeRelist(listing);
        Assert.That(sent.Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(MeretMarketPacket.Error(19, MeretMarketError.AlreadySale, ugcId: listing).ToArray()) }));
        Assert.That(Stored().ListingEndTime, Is.EqualTo(end));
    }

    // Once used, the next expiry is kind 0: the paid relist only; a request for the free one is refused.
    [Test]
    public void UsedFreeRelistExpiresToThePaidRelist() {
        Start(DateTime.Now.AddDays(-1).ToEpochSeconds(), freeRelistUsed: true);
        Load();
        Assert.That(Stored().RelistKind, Is.EqualTo(0));
        Assert.That(Stored().Status, Is.EqualTo(UgcMarketListingStatus.Expired));
        List<byte[]> sent = FreeRelist(listing);
        Assert.That(sent.Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(MeretMarketPacket.Error(19, MeretMarketError.Common, ugcId: listing).ToArray()) }));
        Assert.That(Stored().Status, Is.EqualTo(UgcMarketListingStatus.Expired));
    }

    // The relist ended before the list was loaded again: the request itself finds the time run out.
    [Test]
    public void FreeRelistWithoutALoadStillWorks() {
        Start(DateTime.Now.AddDays(-1).ToEpochSeconds());
        Assert.That(FreeRelist(listing).Select(p => p[2]), Is.EqualTo(new byte[] { 0x13, 0x15 }));
        Assert.That(Stored().FreeRelistUsed, Is.True);
    }

    [Test]
    public void StoppedListingIsKindOneAndNotFree() {
        Start(DateTime.Now.AddDays(3).ToEpochSeconds());
        Stop(listing);
        (byte state, byte kind, long end) = SaleState(test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket));
        Assert.That((state, kind, end), Is.EqualTo(((byte) UgcMarketListingStatus.Expired, UgcMarketItem.StoppedKind, 0L)));
        Assert.That(Stored().RelistKind, Is.EqualTo(UgcMarketItem.StoppedKind));
        List<byte[]> sent = FreeRelist(listing);
        Assert.That(sent.Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(MeretMarketPacket.Error(19, MeretMarketError.Common, ugcId: listing).ToArray()) }));
        Assert.That(Stored().Status, Is.EqualTo(UgcMarketListingStatus.Expired));
    }

    [Test]
    public void RunningListingIsAlreadySale() {
        Start(DateTime.Now.AddDays(3).ToEpochSeconds());
        Assert.That(FreeRelist(listing).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(MeretMarketPacket.Error(19, MeretMarketError.AlreadySale, ugcId: listing).ToArray()) }));
    }

    [Test]
    public void UnknownListingIsNotSale() {
        Start(DateTime.Now.AddDays(-1).ToEpochSeconds());
        Assert.That(FreeRelist(31337).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(MeretMarketPacket.Error(19, MeretMarketError.NotSale, ugcId: 31337).ToArray()) }));
    }

    // The paid relist (18) of a stopped listing clears its kind and spends the free relist: its next expiry is kind 0.
    [Test]
    public void PaidRelistClearsTheKind() {
        Start(DateTime.Now.AddDays(3).ToEpochSeconds());
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meret, 390, 100, 10000),
        }));
        Stop(listing);
        test.TakeSent();
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(18);
            writer.WriteLong(listing);
            writer.WriteLong(500);
            writer.WriteBool(false);
            writer.WriteUnicodeString("tag");
            writer.WriteUnicodeString("description");
            writer.WriteLong(0);
        }));
        UgcMarketItem stored = Stored();
        Assert.That((stored.Status, stored.RelistKind, stored.FreeRelistUsed), Is.EqualTo((UgcMarketListingStatus.Active, (byte) 0, true)));
        test.TakeSent();
        session.UgcMarket.ExpireDue(DateTime.Now.AddDays(SaleDays + 1).ToEpochSeconds());
        (byte state, byte kind, _) = SaleState(test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket));
        Assert.That((state, kind), Is.EqualTo(((byte) UgcMarketListingStatus.Expired, (byte) 0)));
    }

    // A listing whose time runs out while the seller is online is stored expired with the free relist and sent as
    // 0x15 (read unasked by OnProductSaleStateChanged), once; a listing still on sale sends nothing.
    [Test]
    public void ExpiryWhileOnlinePushesTheSaleState() {
        long end = DateTime.Now.AddMinutes(5).ToEpochSeconds();
        Start(end);
        Load();
        session.UgcMarket.ExpireDue(end - 1);
        Assert.That(test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket), Is.Empty);
        Assert.That(Stored().Status, Is.EqualTo(UgcMarketListingStatus.Active));

        session.UgcMarket.ExpireDue(end);
        (byte state, byte kind, long sentEnd) = SaleState(test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket));
        Assert.That((state, kind, sentEnd), Is.EqualTo(((byte) UgcMarketListingStatus.Expired, UgcMarketItem.FreeRelistKind, end)));
        UgcMarketItem stored = Stored();
        Assert.That((stored.Status, stored.RelistKind), Is.EqualTo((UgcMarketListingStatus.Expired, UgcMarketItem.FreeRelistKind)));

        session.UgcMarket.ExpireDue(end + 60);
        Assert.That(test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket), Is.Empty);
        Assert.That(FreeRelist(listing).Select(p => p[2]), Is.EqualTo(new byte[] { 0x13, 0x15 }));
    }

    // A late save of a copy from before the free relist leaves the listing on sale (in-memory store).
    [Test]
    public void OlderCopyDoesNotUndoTheFreeRelist() {
        Start(DateTime.Now.AddDays(-1).ToEpochSeconds());
        Load();
        UgcMarketItem older = Stored();
        Assert.That((older.RelistKind, older.FreeRelistUsed), Is.EqualTo((UgcMarketItem.FreeRelistKind, false)));
        FreeRelist(listing);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveUgcMarketItems([older]), Is.True);
        }
        UgcMarketItem stored = Stored();
        Assert.That((stored.Status, stored.RelistKind, stored.FreeRelistUsed), Is.EqualTo((UgcMarketListingStatus.Active, (byte) 0, true)));
    }

    // CUgcMarketProduct's reader (0x140895690): the int after the three longs that end in the sale's end time is
    // the kind (+0x70).
    [Test]
    public void ProductCarriesTheKindAfterTheEndTime() {
        var item = new UgcMarketItem(TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1)) {
            Status = UgcMarketListingStatus.Expired,
            ListingEndTime = 0x0102030405060708,
            PromotionEndTime = 0,
            SellerAccountId = 1,
            SellerCharacterId = 2,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Id = 5001, Name = "Listed", Url = "item/ugc/listed.png" },
            Blueprint = new ItemBlueprint(),
            RelistKind = UgcMarketItem.FreeRelistKind,
        };
        var writer = new ByteWriter();
        writer.WriteClass<UgcMarketItem>(item);
        byte[] bytes = writer.ToArray();
        int at = Convert.ToHexString(bytes).IndexOf("0807060504030201", StringComparison.Ordinal) / 2;
        Assert.That(at, Is.GreaterThan(0));
        Assert.That(BitConverter.ToInt32(bytes, at + 8), Is.EqualTo(3));
    }
}

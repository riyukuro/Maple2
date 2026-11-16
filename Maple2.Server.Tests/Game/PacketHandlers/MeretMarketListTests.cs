using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MeretMarket 13 (list a design) and 18 (list it again): a refusal answers MeretMarket 0x00 with the client's own
// error code (CMeratMarketMan::OnErrorReply) and takes no merets. The ids are made up.
public class MeretMarketListTests {
    private const int Design = 11300001;
    private const int Town = 90000100;
    private const long DesignUid = 777001;
    private const string BannedWord = "badword";

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long itemUid;
    private long firstListing;

    // A session with a design item of its own in the bag, |listings| listings already on sale (made before Start
    // so the market manager loads them), and the design's price range 100-10000.
    private void Start(int listings = 0, bool survival = false, bool categories = true, long designAccount = -1,
                       long listedDesign = 0, long endTime = 0, bool registrable = true) {
        test = new TestGameSession();
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meret, 390, 100, 10000),
        }));
        // Without |categories| the table has only the premium section.
        test.SetTable(new MeretMarketCategoryTable(new Dictionary<int, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>> {
            [(int) (categories ? MeretMarketSection.Ugc : MeretMarketSection.Premium)] = new Dictionary<int, MeretMarketCategoryTable.Tab> {
                [1] = new([""], false, false, []),
            },
        }));
        ItemMetadata metadata = TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1);
        if (survival) {
            metadata = metadata with { Property = metadata.Property with { Survival = true } };
        }
        if (!registrable) {
            metadata = metadata with { Limit = metadata.Limit with { EnableMeretMarket = false } };
        }
        itemUid = test.Store(metadata).Uid;
        for (int i = 0; i < listings; i++) {
            long id = Listing(metadata, test.AccountId, i == 0 && listedDesign != 0 ? listedDesign : 5000 + i,
                endTime != 0 ? endTime : DateTime.Now.AddDays(7).ToEpochSeconds());
            if (i == 0) {
                firstListing = id;
            }
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Item.Inventory.Get(itemUid)!.Template = new UgcItemLook {
            Id = DesignUid,
            Name = "Design",
            Url = "item/ugc/design.png",
            AccountId = designAccount == -1 ? session.AccountId : designAccount,
        };
        session.Currency.Meret = 5000;
        test.TakeSent();
        storedMeretAtStart = StoredMeret();
    }

    private long storedMeretAtStart;

    private long Listing(ItemMetadata metadata, long accountId, long lookId, long endTime) {
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateUgcMarketItem(new UgcMarketItem(metadata) {
            Price = 500,
            TabId = 1,
            Status = UgcMarketListingStatus.Active,
            ListingEndTime = endTime,
            PromotionEndTime = 0,
            SellerAccountId = accountId,
            SellerCharacterId = test.CharacterId,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Id = lookId, Name = "Listed", Url = "item/ugc/listed.png" },
            Blueprint = new ItemBlueprint(),
        })!.Id;
    }

    // ListItem and RelistItem share a layout: the id, the price, the promote flag, tags, description, the fee.
    private List<byte[]> Send(byte command, long id, long price, string tags = "tag", string description = "description") {
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        words.BanWordMetadata.Add(new BanWordMetadata(0, BannedWord, false));
        words.SaveChanges();
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata, BanWordStorage = new BanWordStorage(words) }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(command);
                writer.WriteLong(id);
                writer.WriteLong(price);
                writer.WriteBool(false);
                writer.WriteUnicodeString(tags);
                writer.WriteUnicodeString(description);
                writer.WriteLong(0);
            }));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
    }

    // ErrorPacket::Read: the command 0x00, then kind, error, the product key (premium id, ugc id), a long and the
    // blueprint flag.
    private static string Error(byte kind, int error, long ugcId) {
        var writer = Packet.Of(SendOp.MeretMarket);
        writer.WriteByte(0x00);
        writer.WriteByte(kind);
        writer.WriteInt(error);
        writer.WriteInt(0);
        writer.WriteLong(ugcId);
        writer.WriteLong(0);
        writer.WriteByte(0);
        return Convert.ToHexString(writer.ToArray());
    }

    private void AssertRefused(List<byte[]> sent, byte kind, int error, long ugcId) {
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Error(kind, error, ugcId) }));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000));
    }

    [Test]
    public void ErrorLayoutIsTheClientReader() {
        Assert.That(Convert.ToHexString(MeretMarketPacket.Error(13, MeretMarketError.RegisterPrice, ugcId: 42).ToArray()),
            Is.EqualTo(Error(13, 0x7D, 42)));
    }

    [Test]
    public void MatchItemIsRefusedAsDisabled() {
        Start(survival: true);
        AssertRefused(Send(13, itemUid, 500), 13, 0x9A, itemUid);
    }

    // FUN_140633d50 (the dialog checks it for designs too): the item data's enableRegisterMeratMarket, and each
    // socketed gem's.
    [TestCase(false)]
    [TestCase(true)]
    public void DesignTheMarketDoesNotTakeIsRefusedAsDisabled(bool gem) {
        Start(registrable: gem);
        if (gem) {
            session.Item.Inventory.Get(itemUid)!.Socket =
                new ItemSocket(1, [new ItemGemstone(MeretMarketMusicScoreTests.UnregistrableGem(test).Id, stats: new ItemStats())]);
        }
        AssertRefused(Send(13, itemUid, 500), 13, 0x9A, itemUid);
        Assert.That(session.UgcMarket.ListingCount, Is.EqualTo(0));
    }

    [Test]
    public void ItemWithoutDesignIsRefusedAsDisabled() {
        Start();
        AssertRefused(Send(13, 99999, 500), 13, 0x9A, 99999);
    }

    [Test]
    public void ForeignDesignIsRefusedAsDisabled() {
        Start(designAccount: 424242);
        AssertRefused(Send(13, itemUid, 500), 13, 0x9A, itemUid);
    }

    [Test]
    public void DesignOnSaleIsRefusedAsAlreadyListed() {
        Start(listings: 1, listedDesign: DesignUid);
        AssertRefused(Send(13, itemUid, 500), 13, 0x98, itemUid);
    }

    [Test]
    public void HundredListingsAreTheLimit() {
        Start(listings: 100);
        AssertRefused(Send(13, itemUid, 500), 13, 0x79, itemUid);
    }

    [Test]
    public void NoUgcCategoryIsRefused() {
        Start(categories: false);
        AssertRefused(Send(13, itemUid, 500), 13, 0x82, itemUid);
    }

    [Test]
    public void PriceOutsideTheRangeIsRefusedNotClamped() {
        Start();
        AssertRefused(Send(13, itemUid, 99), 13, 0x7D, itemUid);
        AssertRefused(Send(13, itemUid, 10001), 13, 0x7D, itemUid);
    }

    [Test]
    public void FeeOverTheMeretsIsLackMerat() {
        Start(listings: 1);
        session.Currency.Meret = 10; // one listing on sale: the fee is 30
        List<byte[]> sent = Send(13, itemUid, 500);
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Error(13, 0x88, itemUid) }));
        Assert.That(session.Currency.Meret, Is.EqualTo(10));
    }

    [Test]
    public void ListingInRangeSendsNoError() {
        Start();
        List<byte[]> sent = Send(13, itemUid, 10000);
        Assert.That(sent.Select(p => p[2]), Does.Not.Contain((byte) 0x00));
        Assert.That(session.UgcMarket.ListingCount, Is.EqualTo(1));
        Assert.That(session.UgcMarket.IsDesignListed(DesignUid), Is.True);
    }

    // A cube placed on one of the player's plots, in the furnishing inventory (the client's cube stock).
    private long Place(UgcItemLook? template) {
        var cube = new PlotCube(TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1), id: 880001, template: template) {
            Type = PlotCube.CubeType.Construction,
        };
        var placed = (System.Collections.Concurrent.ConcurrentDictionary<long, PlotCube>) typeof(Maple2.Server.Game.Manager.Items.FurnishingManager)
            .GetField("inventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(session.Item.Furnishing)!;
        placed[cube.Id] = cube;
        return cube.Id;
    }

    // The register dialog also offers a design whose only copy is placed in the player's home, by the cube's uid
    // (CollectListableItems reads the cube stock); it was refused as not listable. Nothing leaves the plot.
    [Test]
    public void PlacedDesignIsListed() {
        Start();
        long cubeUid = Place(new UgcItemLook { Id = 777002, Name = "Placed", Url = "item/ugc/placed.png", AccountId = session.AccountId });

        List<byte[]> sent = Send(13, cubeUid, 500);

        Assert.Multiple(() => {
            Assert.That(sent.Select(p => p[2]), Does.Not.Contain((byte) 0x00));
            Assert.That(session.UgcMarket.IsDesignListed(777002), Is.True);
            Assert.That(session.Item.Furnishing.GetPlacedCube(cubeUid), Is.Not.Null);
        });
    }

    // A placed cube with no design, or another account's design, is refused as for a stack.
    [TestCase(false)]
    [TestCase(true)]
    public void PlacedCubeWithoutOwnDesignIsRefused(bool foreign) {
        Start();
        long cubeUid = Place(foreign ? new UgcItemLook { Id = 777003, Name = "Foreign", Url = "item/ugc/foreign.png", AccountId = 424242 } : null);
        AssertRefused(Send(13, cubeUid, 500), 13, 0x9A, cubeUid);
    }

    [Test]
    public void RelistOfUnknownListingIsNotSale() {
        Start();
        AssertRefused(Send(18, 31337, 500), 18, 0x77, 31337);
    }

    [Test]
    public void RelistOfRunningListingIsAlreadySale() {
        Start(listings: 1);
        long id = firstListing;
        AssertRefused(Send(18, id, 500), 18, 0x8E, id);
    }

    [Test]
    public void RelistPriceOutsideTheRangeIsRefused() {
        Start(listings: 1, endTime: DateTime.Now.AddDays(-1).ToEpochSeconds());
        long id = firstListing;
        AssertRefused(Send(18, id, 20000), 18, 0x7D, id);
    }

    // A listing save the database refuses: the fee is taken first, so it must come back, once.
    private void FailMarketSaves() =>
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.Entity.GetType().Name == "UgcMarketItem");

    [Test]
    public void ListingTheDatabaseRefusesIsCommonAndRefunded() {
        Start(listings: 1); // one listing on sale: the fee is 30
        FailMarketSaves();
        AssertRefused(Send(13, itemUid, 500), 13, 0x68, itemUid);
        Assert.That(session.UgcMarket.ListingCount, Is.EqualTo(1));
        Assert.That(session.UgcMarket.IsDesignListed(DesignUid), Is.False);
    }

    [Test]
    public void RelistTheDatabaseRefusesIsCommonAndRestored() {
        long ended = DateTime.Now.AddDays(-1).ToEpochSeconds();
        Start(listings: 1, endTime: ended); // the listing still counts as on sale: the fee is 30
        long id = firstListing;
        FailMarketSaves();
        AssertRefused(Send(18, id, 800), 18, 0x68, id);
        Assert.That(session.UgcMarket.TryGetItem(id, out UgcMarketItem? item), Is.True);
        Assert.Multiple(() => {
            Assert.That(item!.Price, Is.EqualTo(500));
            Assert.That(item.ListingEndTime, Is.EqualTo(ended));
            Assert.That(item.PromotionEndTime, Is.EqualTo(0));
            Assert.That(item.Status, Is.EqualTo(UgcMarketListingStatus.Active));
            Assert.That(item.Description, Is.EqualTo(string.Empty));
            Assert.That(item.Tags, Is.Empty);
        });
    }

    // The wallet's row (Account, Character) cannot be stored, as a crash between the listing's row and the wallet.
    private void FailWalletSaves() =>
        test.FailSave = context => context.ChangeTracker.Entries()
            .Any(entry => entry.State == EntityState.Modified && entry.Entity is Maple2.Database.Model.Account or Maple2.Database.Model.Character);

    private long StoredMeret() {
        using var context = new Ms2Context(test.Options);
        return context.Account.AsNoTracking().Single(row => row.Id == test.AccountId).Currency.Meret;
    }

    private int StoredListings() {
        using var context = new Ms2Context(test.Options);
        return context.UgcMarketItem.AsNoTracking().Count();
    }

    // The fee and the listing are stored together: the stored wallet has the fee taken once the listing is there.
    [Test]
    public void ListingFeeIsStoredWithTheListing() {
        Start(listings: 1); // one listing on sale: the fee is 30

        Send(13, itemUid, 500);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(4970));
            Assert.That(StoredMeret(), Is.EqualTo(4970));
            Assert.That(StoredListings(), Is.EqualTo(2));
        });
    }

    // The wallet cannot be stored: nothing is listed and the fee stays. Before, the listing's row was stored on its
    // own and the wallet after it, so the listing was kept with the fee never stored (a free listing after a crash).
    [Test]
    public void ListingWhoseWalletIsNotStoredListsNothing() {
        Start(listings: 1);
        FailWalletSaves();

        AssertRefused(Send(13, itemUid, 500), 13, 0x68, itemUid);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.UgcMarket.IsDesignListed(DesignUid), Is.False);
            Assert.That(StoredListings(), Is.EqualTo(1));
            Assert.That(StoredMeret(), Is.EqualTo(StoredMeretBefore));
        });
    }

    [Test]
    public void RelistFeeIsStoredWithTheListing() {
        Start(listings: 1, endTime: DateTime.Now.AddDays(-1).ToEpochSeconds());

        Send(18, firstListing, 800);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(4970));
            Assert.That(StoredMeret(), Is.EqualTo(4970));
            Assert.That(StoredListing(firstListing).Price, Is.EqualTo(800));
        });
    }

    [Test]
    public void RelistWhoseWalletIsNotStoredKeepsTheListing() {
        long ended = DateTime.Now.AddDays(-1).ToEpochSeconds();
        Start(listings: 1, endTime: ended);
        FailWalletSaves();

        AssertRefused(Send(18, firstListing, 800), 18, 0x68, firstListing);
        test.FailSave = null;

        Assert.That(session.UgcMarket.TryGetItem(firstListing, out UgcMarketItem? item), Is.True);
        Assert.Multiple(() => {
            Assert.That((item!.Price, item.ListingEndTime), Is.EqualTo((500L, ended)));
            Assert.That(StoredListing(firstListing).Price, Is.EqualTo(500));
            Assert.That(StoredListing(firstListing).ListingEndTime, Is.EqualTo(ended));
            Assert.That(StoredMeret(), Is.EqualTo(StoredMeretBefore));
        });
    }

    // The wallet the store held before the step (the session's own row as Start left it).
    private long StoredMeretBefore => storedMeretAtStart;

    private UgcMarketItem StoredListing(long id) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetUgcMarketItem(id)!;
    }

    // The dialogs' ValidateTexts, asked again here: a banned word in the tag line or the description, or a
    // description over 200 characters, is refused with the ban word reply (0xAD) before any fee.
    [TestCase("tag,badword", "description")]
    [TestCase("tag", "a BADWORD here")]
    public void BannedWordIsRefused(string tags, string description) {
        Start();
        AssertRefused(Send(13, itemUid, 500, tags, description), 13, 0xAD, itemUid);
        Assert.That(session.UgcMarket.ListingCount, Is.EqualTo(0));
    }

    [Test]
    public void DescriptionOverTwoHundredIsRefused() {
        Start();
        AssertRefused(Send(13, itemUid, 500, description: new string('a', 201)), 13, 0xAD, itemUid);
        Assert.That(Send(13, itemUid, 500, description: new string('a', 200)).Select(p => p[2]), Does.Not.Contain((byte) 0x00));
        Assert.That(session.UgcMarket.ListingCount, Is.EqualTo(1));
    }

    [Test]
    public void RelistWithABannedWordIsRefused() {
        Start(listings: 1, endTime: DateTime.Now.AddDays(-1).ToEpochSeconds());
        long id = firstListing;
        AssertRefused(Send(18, id, 800, tags: "badword"), 18, 0xAD, id);
        Assert.That(session.UgcMarket.TryGetItem(id, out UgcMarketItem? item), Is.True);
        Assert.That(item!.Price, Is.EqualTo(500));
    }

    // A search word in two of a listing's tags finds the listing once.
    [Test]
    public void SearchFindsAListingOnce() {
        var listing = new UgcMarketItem(TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1)) {
            Status = UgcMarketListingStatus.Active,
            ListingEndTime = 0,
            PromotionEndTime = 0,
            SellerAccountId = 1,
            SellerCharacterId = 1,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = ["red", "reddish"],
            Look = new UgcItemLook { Id = 1, Name = "Coat", Url = "" },
            Blueprint = new ItemBlueprint(),
        };
        Assert.That(MeretMarketHandler.FilterByString([listing], "red").Count(), Is.EqualTo(1));
    }
}

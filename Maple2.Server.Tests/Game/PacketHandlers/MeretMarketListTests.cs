using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MeretMarket 13 (list a design) and 18 (list it again): a refusal answers MeretMarket 0x00 with the client's own
// error code (CMeratMarketMan::OnErrorReply) and takes no merets. The ids are made up.
public class MeretMarketListTests {
    private const int Design = 11300001;
    private const int Town = 90000100;
    private const long DesignUid = 777001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long itemUid;
    private long firstListing;

    // A session with a design item of its own in the bag, |listings| listings already on sale (made before Start
    // so the market manager loads them), and the design's price range 100-10000.
    private void Start(int listings = 0, bool survival = false, bool categories = true, long designAccount = -1,
                       long listedDesign = 0, long endTime = 0) {
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
    }

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
    private List<byte[]> Send(byte command, long id, long price) {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(command);
                writer.WriteLong(id);
                writer.WriteLong(price);
                writer.WriteBool(false);
                writer.WriteUnicodeString("tag");
                writer.WriteUnicodeString("description");
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
}

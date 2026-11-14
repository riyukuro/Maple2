using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// UGC market music scores (UGCMarketMusicScore): MeretMarket 13 lists a composed score the register dialog offers
// (FUN_1406a5a50), and MeretMarket 30 gives the buyer a locked copy (SetFromScoreInfo). The ids are made up; 35100001
// is a custom score (group 3, type 51).
public class MeretMarketMusicScoreTests {
    private const int Score = 35100001;
    private const int Town = 90000100;
    private const int PlayCount = 10;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private long scoreUid;

    private static ItemMetadata ScoreMetadata() => TestGameSession.Metadata(Score, slotMax: 1) with {
        Music = new ItemMetadataMusic(PlayCount, 1, 1, true, 1000, string.Empty, 0),
    };

    private void Start(bool feature = true) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        if (!feature) {
            test.SetFeaturesOff(MeretMarketHandler.MusicScoreFeature);
        }
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry>()));
        test.SetTable(new MeretMarketCategoryTable(new Dictionary<int, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>> {
            [(int) MeretMarketSection.Ugc] = new Dictionary<int, MeretMarketCategoryTable.Tab> {
                [1] = new([""], false, false, []),
            },
        }));
        scoreUid = test.Store(ScoreMetadata()).Uid;
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        Item score = session.Item.Inventory.Get(scoreUid)!;
        score.Rarity = 2;
        score.Music = new ItemCustomMusicScore {
            Length = 12000,
            Instrument = (Instrument) 3,
            Title = "Song",
            Author = "Writer",
            AuthorId = session.AccountId,
            Mml = "T120 c4d4e4",
        };
        session.Currency.Meret = 5000;
        test.TakeSent();
    }

    private Item OwnScore => session.Item.Inventory.Get(scoreUid)!;

    private List<byte[]> List(long uid, long price = 15) {
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata, BanWordStorage = new BanWordStorage(words) }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(13);
                writer.WriteLong(uid);
                writer.WriteLong(price);
                writer.WriteBool(false);
                writer.WriteUnicodeString("tag");
                writer.WriteUnicodeString("description");
                writer.WriteLong(0);
            }));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
    }

    // The refusal of MeretMarket 13: 0x00, the command, the error, then the product key.
    private static void AssertRefused(List<byte[]> sent, int error) {
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(sent[0][2], Is.EqualTo(0x00));
            Assert.That(sent[0][3], Is.EqualTo(13));
            Assert.That(BitConverter.ToInt32(sent[0], 4), Is.EqualTo(error));
        });
    }

    [Test]
    public void ComposedScoreIsListedWithItsScore() {
        Start();
        List<byte[]> sent = List(scoreUid);

        Assert.That(sent.Select(p => p[2]), Does.Not.Contain((byte) 0x00));
        Assert.That(session.UgcMarket.IsScoreListed(scoreUid), Is.True);
        using GameStorage.Request db = test.Storage.Context();
        UgcMarketItem listing = db.GetUgcListingsByAccountId(session.AccountId).Values.Single();
        Assert.Multiple(() => {
            Assert.That(listing.IsMusicScore, Is.True);
            Assert.That(listing.MusicScore.ItemUid, Is.EqualTo(scoreUid));
            Assert.That(listing.MusicScore.Title, Is.EqualTo("Song"));
            Assert.That(listing.MusicScore.Author, Is.EqualTo("Writer"));
            Assert.That(listing.MusicScore.Mml, Is.EqualTo("T120 c4d4e4"));
            Assert.That(listing.MusicScore.Length, Is.EqualTo(12000));
            Assert.That(listing.MusicScore.Instrument, Is.EqualTo(3));
            Assert.That(listing.MusicScore.AuthorId, Is.EqualTo(session.AccountId));
            Assert.That(listing.Name, Is.EqualTo("Song"));
            Assert.That(OwnScore.Music, Is.Not.Null, "the seller keeps the score");
        });
    }

    [Test]
    public void ListedScoreIsAlreadyListed() {
        Start();
        List(scoreUid);
        AssertRefused(List(scoreUid), MeretMarketError.AlreadyListed);
    }

    [TestCase("feature")]
    [TestCase("locked")]
    [TestCase("author")]
    [TestCase("played")]
    [TestCase("rarity")]
    [TestCase("blank")]
    public void ScoreTheDialogDoesNotOfferIsRefused(string reason) {
        Start(feature: reason != "feature");
        switch (reason) {
            case "locked":
                OwnScore.Music!.IsLocked = true;
                break;
            case "author":
                OwnScore.Music!.AuthorId = session.AccountId + 1;
                break;
            case "played":
                OwnScore.RemainUses = PlayCount - 1;
                break;
            case "rarity":
                OwnScore.Rarity = 3;
                break;
            case "blank":
                OwnScore.Music = null;
                break;
        }

        AssertRefused(List(scoreUid), MeretMarketError.DisableUgcItem);
        Assert.Multiple(() => {
            Assert.That(session.UgcMarket.ListingCount, Is.EqualTo(0));
            Assert.That(session.Currency.Meret, Is.EqualTo(5000));
        });
    }

    // A score listed by another account: the buyer pays the price and gets a locked copy with the score's notes.
    [Test]
    public void BoughtScoreIsALockedCopy() {
        Start();
        long id;
        using (GameStorage.Request db = test.Storage.Context()) {
            id = db.CreateUgcMarketItem(new UgcMarketItem(ScoreMetadata()) {
                Price = 300,
                TabId = 1,
                Status = UgcMarketListingStatus.Active,
                ListingEndTime = DateTime.Now.AddDays(7).ToEpochSeconds(),
                PromotionEndTime = 0,
                SellerAccountId = session.AccountId + 1,
                SellerCharacterId = 1,
                SellerCharacterName = "Seller",
                Description = string.Empty,
                Tags = [],
                Look = new UgcItemLook(),
                Blueprint = new ItemBlueprint(),
                MusicScore = new UgcItemMusicScore {
                    ItemUid = 4242, Length = 9000, Instrument = 5, Author = "Seller", Title = "Tune", Mml = "T100 e8",
                    AuthorType = ItemCustomMusicScore.AccountAuthorType, AuthorId = session.AccountId + 1,
                },
            })!.Id;
        }

        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata, BanWordStorage = null! }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(30);
                writer.WriteByte(1);
                writer.WriteInt(0);
                writer.WriteLong(id);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteInt(1);
                writer.WriteInt(0);
                writer.WriteBool(false);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteLong(300);
                writer.WriteBool(false);
                writer.WriteByte(3);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
            }));

        Item? copy = session.Item.Inventory.Filter(item => item.Id == Score && item.Uid != scoreUid).SingleOrDefault();
        Assert.That(copy?.Music, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(4700));
            Assert.That(copy!.Music!.IsLocked, Is.True);
            Assert.That(copy.Music.Title, Is.EqualTo("Tune"));
            Assert.That(copy.Music.Author, Is.EqualTo("Seller"));
            Assert.That(copy.Music.Mml, Is.EqualTo("T100 e8"));
            Assert.That(copy.Music.Length, Is.EqualTo(9000));
            Assert.That((int) copy.Music.Instrument, Is.EqualTo(5));
            Assert.That(copy.Music.AuthorId, Is.EqualTo(session.AccountId + 1));
            Assert.That(copy.Rarity, Is.EqualTo(2));
            Assert.That(copy.Template, Is.Null);
        });
    }

    // CUgcItemMusicNote::Read (ugcdef.cpp:1727): uid, length, instrument, the writer, the title, the notes (ascii), the
    // owner type and id, a long and a string.
    [Test]
    public void ScoreBlockIsTheClientReader() {
        var score = new UgcItemMusicScore {
            ItemUid = 7, Length = 8, Instrument = 9, Author = "A", Title = "T", Mml = "c", AuthorType = 1, AuthorId = 10,
        };
        var writer = new ByteWriter();
        score.WriteTo(writer);

        var expected = new ByteWriter();
        expected.WriteLong(7);
        expected.WriteInt(8);
        expected.WriteInt(9);
        expected.WriteUnicodeString("A");
        expected.WriteUnicodeString("T");
        expected.WriteString("c");
        expected.WriteInt(1);
        expected.WriteLong(10);
        expected.WriteLong(0);
        expected.WriteUnicodeString(string.Empty);
        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Convert.ToHexString(expected.ToArray())));
    }
}

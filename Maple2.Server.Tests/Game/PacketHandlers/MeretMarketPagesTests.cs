using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

// The design market's page requests, written as the KR client writes them (meratmarketman.cpp):
// - 12 asks for the seller list (my settlements), answered by 0x0c (OnSellerList, CUgcMarketSeller::Read).
// - 27 (SendMeretMarketMode27), 28 / 105 (SendSearchCondition, 105 with a text and a trailing true) and 104
//   (SendMeretMarketMode104, with a section byte) send the search condition (CMeratMarketSchCond::Write); 27 and 104
//   are answered by the search page 0x1b (OnSearchResultPage), 28 and 105 by the blueprint page 0x1c
//   (OnBlueprintPage).
// - 29 asks for one product by id (premium flag, long id, the blueprint dialog flag).
// - 102 asks for the design home lists, answered by 0x66 (OnUgcProductsPacket).
// The requests copy the KMS2 captures' bytes but the "all" tab (1), which needs no category rows; the ids are made up.
public class MeretMarketPagesTests {
    private const int Design = 11300002;
    private const int Town = 90000100;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1));
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        SetPremiumMarket();
        test.TakeSent();
    }

    private void SetPremiumMarket() {
        object server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var table = new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata>());
        typeof(Maple2.Server.Game.GameServer).GetField("premiumMarketCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, Maple2.Server.Game.GameServer.PremiumMarket(table, id => session.ItemMetadata.TryGet(id, out ItemMetadata? item) ? item : null));
    }

    private long List(long endTime, long promotionEndTime = 0) {
        session.ItemMetadata.TryGet(Design, out ItemMetadata? metadata);
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateUgcMarketItem(new UgcMarketItem(metadata!) {
            Price = 3000,
            TabId = 1,
            Status = UgcMarketListingStatus.Active,
            ListingEndTime = endTime,
            PromotionEndTime = promotionEndTime,
            SellerAccountId = 1,
            SellerCharacterId = 1,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Name = "Design", Url = "item/ugc/design.png" },
            Blueprint = new ItemBlueprint(),
        })!.Id;
    }

    private static long Later => DateTime.Now.AddDays(1).ToEpochSeconds();

    private List<byte[]> Send(Action<ByteWriter> body) {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(body));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
    }

    // CMeratMarketSchCond::Write: tab, gender, job, sort, text, the first page of the block (+0x30), the page asked
    // for (+0x34), currency, a byte and the page size. The KMS2 search for all designs: 03 ff0f0000 04 "" ... 00 00 12.
    private static void Condition(ByteWriter writer, byte mode, int startPage, int page, byte perPage, string text = "") {
        writer.WriteByte(mode);
        writer.WriteInt(1);
        writer.WriteByte(3);
        writer.WriteInt(0xfff);
        writer.WriteByte(4);
        writer.WriteUnicodeString(text);
        writer.WriteInt(startPage);
        writer.WriteInt(page);
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteByte(perPage);
    }

    private List<byte[]> Search(byte mode, int startPage, int page, byte perPage) => Send(writer => {
        Condition(writer, mode, startPage, page, perPage, mode == 105 ? "\u0044\u0065\u0073\u0069\u0067\u006e" : "");
        if (mode == 105) {
            writer.WriteBool(true);
        }
        if (mode == 104) {
            writer.WriteByte(3); // the design section
        }
    });

    private static int Int(byte[] packet, int offset) => BitConverter.ToInt32(packet, offset);

    // 27 opens the market on page 1 (KMS2: 1b cc000000 03 ff0f0000 04 0000 01000000 01000000 00 00 12): the answer
    // 0x1b holds the designs on sale with their total and the page; an ended listing is not offered.
    [Test]
    public void OpenShop_AnswersTheFirstPage() {
        List(Later);
        List(Later);
        List(DateTime.Now.AddDays(-1).ToEpochSeconds());

        byte[] answer = Search(27, 1, 1, 0x12).Single();
        Assert.Multiple(() => {
            Assert.That(answer[2], Is.EqualTo(0x1b));
            Assert.That((Int(answer, 3), Int(answer, 7), Int(answer, 11)), Is.EqualTo((2, 2, 1)));
        });
    }

    // A page further on is a block of five pages from the first page of the block: 22 designs at 2 a page, page 6
    // holds the 11th design onward, 10 of them (pages 6-10). The client places the page by its number within the
    // block (nFirst = ((page - 1) % 5) * size).
    [TestCase((byte) 27, (byte) 0x1b)]
    [TestCase((byte) 104, (byte) 0x1b)]
    [TestCase((byte) 28, (byte) 0x1c)]
    [TestCase((byte) 105, (byte) 0x1c)]
    public void APageOn_AnswersItsBlockOfFive(byte mode, byte answerMode) {
        for (int i = 0; i < 22; i++) {
            List(Later);
        }

        byte[] answer = Search(mode, 6, 6, 2).Single();
        int pageAt = answerMode == 0x1c ? 12 : 11;
        Assert.Multiple(() => {
            Assert.That(answer[2], Is.EqualTo(answerMode));
            Assert.That((Int(answer, 3), Int(answer, 7), Int(answer, pageAt)), Is.EqualTo((10, 22, 6)));
        });
    }

    // Going back a page (UIMeratMarketDialog::OnChangePageRequest with a negative direction sets +0x44) the client
    // asks for the page before the block, 5 from page 6, and sends the block's first page, 5 - 4 = 1, as the start:
    // the answer holds pages 1-5 and must say page 5, the one asked for, so the client shows the block's last page.
    // Before, the answer said page 1 and the client showed the first page of the block.
    [TestCase((byte) 27, (byte) 0x1b)]
    [TestCase((byte) 104, (byte) 0x1b)]
    [TestCase((byte) 28, (byte) 0x1c)]
    [TestCase((byte) 105, (byte) 0x1c)]
    public void APageBack_AnswersThePageAskedFor(byte mode, byte answerMode) {
        for (int i = 0; i < 22; i++) {
            List(Later);
        }

        byte[] answer = Search(mode, 1, 5, 2).Single();
        int pageAt = answerMode == 0x1c ? 12 : 11;
        Assert.Multiple(() => {
            Assert.That(answer[2], Is.EqualTo(answerMode));
            Assert.That((Int(answer, 3), Int(answer, 7), Int(answer, pageAt)), Is.EqualTo((10, 22, 5)));
        });
    }

    // The blueprint page carries the page size after the total (OnBlueprintPage): the one the condition asked for.
    [Test]
    public void BlueprintPage_CarriesTheAskedPageSize() {
        List(Later);

        byte[] answer = Search(28, 1, 1, 8).Single();
        Assert.That((answer[2], Int(answer, 3), Int(answer, 7), answer[11], Int(answer, 12)), Is.EqualTo(((byte) 0x1c, 1, 1, (byte) 8, 1)));
    }

    // 29 from the blueprint dialog (KMS2: 1d 00 010030dcbc252f24 01) is answered by a blueprint page of the one
    // design with page size 18, as KMS2 answers it (1c 01000000 01000000 12 01000000). Before, the size was 1, so the
    // one design filled the page and the dialog offered a next page (isEndPage false).
    [Test]
    public void FindItem_FromTheBlueprintDialog_AnswersAPageOf18() {
        long id = List(Later);

        byte[] answer = Send(writer => {
            writer.WriteByte(29);
            writer.WriteBool(false);
            writer.WriteLong(id);
            writer.WriteBool(true);
        }).Single();
        Assert.That((answer[2], Int(answer, 3), Int(answer, 7), answer[11], Int(answer, 12)), Is.EqualTo(((byte) 0x1c, 1, 1, (byte) 0x12, 1)));
    }

    // 29 from the market dialog is answered by a search page of the one design; an unknown id gets no answer.
    [Test]
    public void FindItem_FromTheMarketDialog_AnswersASearchPage() {
        long id = List(Later);

        byte[] answer = Send(writer => {
            writer.WriteByte(29);
            writer.WriteBool(false);
            writer.WriteLong(id);
            writer.WriteBool(false);
        }).Single();
        List<byte[]> unknown = Send(writer => {
            writer.WriteByte(29);
            writer.WriteBool(false);
            writer.WriteLong(id + 100);
            writer.WriteBool(false);
        });
        Assert.Multiple(() => {
            Assert.That((answer[2], Int(answer, 3), Int(answer, 7), Int(answer, 11)), Is.EqualTo(((byte) 0x1b, 1, 1, 1)));
            Assert.That(unknown, Is.Empty);
        });
    }

    // One design as WriteMarketItem writes it inside a page: the LoadItems body after its count, total and page.
    private string Entry(long id, UgcMarketHomeCategory category) {
        using GameStorage.Request db = test.Storage.Context();
        UgcMarketItem item = db.GetUgcMarketItem(id)!;
        item.Category = category;
        return Convert.ToHexString(MeretMarketPacket.LoadItems([item], 1, 1).ToArray()[15..]);
    }

    // 102 (the design home) is answered by 0x66: the promoted designs with category 1 (the client's promoted list)
    // and the newest designs with category 2 (its new list); an ended listing is in neither.
    [Test]
    public void UgcHome_ListsThePromotedAndTheNew() {
        long promoted = List(Later, DateTime.Now.AddHours(1).ToEpochSeconds());
        long plain = List(Later);
        long ended = List(DateTime.Now.AddDays(-1).ToEpochSeconds());

        byte[] answer = Send(writer => writer.WriteByte(102)).Single();
        string hex = Convert.ToHexString(answer);
        Assert.Multiple(() => {
            Assert.That(answer[2], Is.EqualTo(0x66));
            Assert.That(Int(answer, 3), Is.EqualTo(3)); // the promoted design is also among the new
            Assert.That(hex, Does.Contain(Entry(promoted, UgcMarketHomeCategory.Promoted)));
            Assert.That(hex, Does.Contain(Entry(promoted, UgcMarketHomeCategory.New)));
            Assert.That(hex, Does.Contain(Entry(plain, UgcMarketHomeCategory.New)));
            Assert.That(hex, Does.Not.Contain(Entry(plain, UgcMarketHomeCategory.Promoted)));
            Assert.That(hex, Does.Not.Contain(Entry(ended, UgcMarketHomeCategory.New)));
        });
    }

    // 12 with no sale is answered as KMS2 answers it: 0c 00000000.
    [Test]
    public void Sales_NoneIsAnEmptyList() {
        byte[] answer = Send(writer => writer.WriteByte(12)).Single();
        Assert.That(Convert.ToHexString(answer[2..]), Is.EqualTo("0C00000000"));
    }

    // 12 with a sale: CUgcMarketSeller::Read reads the sale id, a long, the name, an int, the user (int, two longs,
    // two strings), an int, then the price, the sold time and the profit.
    [Test]
    public void Sales_ASaleIsReadAsTheClientReadsIt() {
        long listing = List(Later);
        long soldTime = DateTime.Now.AddDays(-1).ToEpochSeconds();
        long sale;
        using (GameStorage.Request db = test.Storage.Context()) {
            sale = db.AddUgcMarketSale(listing, new SoldUgcMarketItem {
                AccountId = test.AccountId,
                Price = 3000,
                Name = "\u0053\u006f\u006c\u0064",
                Profit = 2700,
                SoldTime = soldTime,
            })!.Id;
        }

        byte[] answer = Send(writer => writer.WriteByte(12)).Single();
        var reader = new ByteReader(answer, 3);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(sale));
            reader.ReadLong();
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("\u0053\u006f\u006c\u0064"));
            reader.ReadInt();
            reader.ReadInt();
            reader.ReadLong();
            reader.ReadLong();
            reader.ReadUnicodeString();
            reader.ReadUnicodeString();
            reader.ReadInt();
            Assert.That((reader.ReadLong(), reader.ReadLong(), reader.ReadLong()), Is.EqualTo((3000L, soldTime, 2700L)));
            Assert.That(reader.Available, Is.Zero);
        });
    }

    // The random-sale requests and 201 as the client writes them: 111 / 112 (product, two bytes, count, seconds,
    // price), 114 (product, type), 115 / 116 (product, rate, notice), 118 (product, an int; KMS2 sends it at a bill
    // open as 76 ddd79d3b 00000000 and gets no answer) and 201 (no body, the GM command RequestSaleMarket). The client
    // reads no answer to any but 114 (0x72, a pick for a random-sale product, which no KR shop row is); none is sent
    // and nothing is paid.
    [TestCase((byte) 111)]
    [TestCase((byte) 112)]
    [TestCase((byte) 114)]
    [TestCase((byte) 115)]
    [TestCase((byte) 116)]
    [TestCase((byte) 118)]
    [TestCase((byte) 201)]
    public void RandomSaleAndSaleMarket_GetNoAnswer(byte mode) {
        session.Currency.Meret = 1000;
        session.Currency.GameMeret = 1000;

        List<byte[]> answers = Send(writer => {
            writer.WriteByte(mode);
            switch (mode) {
                case 111 or 112:
                    writer.WriteInt(0x3b9dd7dd);
                    writer.WriteByte(1);
                    writer.WriteByte(0);
                    writer.WriteInt(3);
                    writer.WriteLong(3600);
                    writer.WriteLong(100);
                    break;
                case 114:
                    writer.WriteInt(0x3b9dd7dd);
                    writer.WriteByte(7);
                    break;
                case 115 or 116:
                    writer.WriteInt(0x3b9dd7dd);
                    writer.WriteInt(50);
                    writer.WriteInt(0);
                    break;
                case 118:
                    writer.WriteInt(0x3b9dd7dd);
                    writer.WriteInt(0);
                    break;
            }
        });
        Assert.Multiple(() => {
            Assert.That(answers, Is.Empty);
            Assert.That((session.Currency.Meret, session.Currency.GameMeret), Is.EqualTo((1000L, 1000L)));
        });
    }
}

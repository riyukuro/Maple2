using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MeretMarket 32 (premium ids) and 33 (UGC listing ids), sent for a chat link with several market serials: the
// answer 0x20 / 0x21 is a search page (the LoadItems body) holding only the ids the market pages would offer.
// The ids are made up.
public class MeretMarketByIdTests {
    private const int Design = 11300001;
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
        test.TakeSent();
    }

    private long List(UgcMarketListingStatus status, long endTime) {
        session.ItemMetadata.TryGet(Design, out ItemMetadata? metadata);
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateUgcMarketItem(new UgcMarketItem(metadata!) {
            Price = 3000,
            TabId = 1,
            Status = status,
            ListingEndTime = endTime,
            PromotionEndTime = 0,
            SellerAccountId = 1,
            SellerCharacterId = 1,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Name = "Design", Url = "item/ugc/design.png" },
            Blueprint = new ItemBlueprint(),
        })!.Id;
    }

    private static MeretMarketItemMetadata Premium(int id, long saleStart, long saleEnd) {
        return new MeretMarketItemMetadata(id, 0, 1, string.Empty, MeretMarketBannerTag.None, Design, 1, 1, 0, 0,
            MeretMarketItemSaleTag.None, MeretMarketCurrencyType.Meret, 100, 0, saleStart, saleEnd, JobFilterFlag.None,
            false, 0, 0, 0, 0, false, false, false, string.Empty, 0, 0);
    }

    private void SetPremiumMarket(params MeretMarketItemMetadata[] entries) {
        object server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var table = new MeretMarketTable(entries.ToDictionary(entry => entry.Id));
        typeof(Maple2.Server.Game.GameServer).GetField("premiumMarketCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, Maple2.Server.Game.GameServer.PremiumMarket(table, id => session.ItemMetadata.TryGet(id, out ItemMetadata? item) ? item : null));
    }

    private List<byte[]> Send(byte command, Action<Maple2.PacketLib.Tools.ByteWriter> ids) {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(command);
                ids(writer);
            }));
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
    }

    // The LoadItems page of |items| (page 1, total = count) with the answer's command byte.
    private static string Page(byte command, params MarketItem[] items) {
        byte[] page = MeretMarketPacket.LoadItems(items, items.Length, 1).ToArray();
        page[2] = command;
        return Convert.ToHexString(page);
    }

    [Test]
    public void UgcIdsListOnlyActiveListingsStillRunning() {
        long now = DateTime.Now.ToEpochSeconds();
        long first = List(UgcMarketListingStatus.Active, now + 3600);
        long second = List(UgcMarketListingStatus.Active, now + 7200);
        long timedOut = List(UgcMarketListingStatus.Active, now - 60);
        long stopped = List(UgcMarketListingStatus.Expired, now + 3600);
        long held = List(UgcMarketListingStatus.Hold, now + 3600);
        long removed = 987654;

        List<byte[]> sent = Send(33, writer => {
            long[] ids = [timedOut, first, stopped, held, removed, second];
            writer.WriteInt(ids.Length);
            foreach (long id in ids) {
                writer.WriteLong(id);
            }
        });

        using GameStorage.Request db = test.Storage.Context();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Page(0x21, db.GetUgcMarketItem(first)!, db.GetUgcMarketItem(second)!)));
    }

    [Test]
    public void PremiumIdsListOnlyItemsInsideTheirSaleWindow() {
        long now = DateTime.Now.ToEpochSeconds();
        SetPremiumMarket(
            Premium(1001, now - 3600, now + 3600),
            Premium(1002, now + 3600, 0),
            Premium(1003, 0, now - 3600),
            Premium(1004, 0, 0));

        List<byte[]> sent = Send(32, writer => {
            int[] ids = [1002, 1001, 1003, 999, 1004];
            writer.WriteInt(ids.Length);
            foreach (int id in ids) {
                writer.WriteInt(id);
            }
        });

        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(Convert.ToHexString(sent[0]),
            Is.EqualTo(Page(0x20, session.GetPremiumMarketItem(1001)!, session.GetPremiumMarketItem(1004)!)));
    }

    // Nothing found still answers, with an empty first page.
    [Test]
    public void UnknownIdsAnswerAnEmptyPage() {
        SetPremiumMarket();

        List<byte[]> sent = Send(32, writer => {
            writer.WriteInt(1);
            writer.WriteInt(999);
        });

        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Convert.ToHexString(new byte[] {
            (byte) SendOp.MeretMarket, (byte) ((int) SendOp.MeretMarket >> 8), 0x20, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
        })));
    }

    [TestCase((byte) 32, 21)]
    [TestCase((byte) 32, -1)]
    [TestCase((byte) 33, 21)]
    [TestCase((byte) 33, -1)]
    public void ACountOutsideTheClientLimitIsNotAnswered(byte command, int count) {
        SetPremiumMarket();

        List<byte[]> sent = Send(command, writer => {
            writer.WriteInt(count);
            for (int i = 0; i < Math.Max(count, 0); i++) {
                if (command == 32) {
                    writer.WriteInt(999);
                } else {
                    writer.WriteLong(999);
                }
            }
        });

        Assert.That(sent, Is.Empty);
    }
}

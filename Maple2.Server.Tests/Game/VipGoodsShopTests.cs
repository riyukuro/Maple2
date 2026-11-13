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
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The VIP goods shop (PremiumClub 0xeb, UIVIPGoodsShopDialog): mode 3 is Int32 count + count x {goodsId, purchases
// left} for the goods with a purchase limit, mode 4 the goods id just bought. The purchase count is stored in the
// account row with the charge and the bonus items, or nothing is.
public class VipGoodsShopTests {
    private const int LimitedId = 2001;
    private const int FreeId = 2002;
    private const int RedId = 2003;
    private const int EndedId = 2004;
    private const int ComingId = 2005;
    private const int NoBonusId = 2006;
    private const int RedLimitedId = 2007;
    private const int OldDbId = 2008;
    private const int NoSalePriceId = 2009;
    private const int PeriodId = 2010;
    private const int LimitDays = 60;
    private const int PeriodMinutes = 1440;
    private const int UnknownItemId = 50999999;
    private const int BonusItemId = 50000104;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private static PremiumClubTable.Package Package(long salePrice, bool red = false, int buyLimit = 0, ResetType reset = ResetType.Default,
                                                    params PremiumClubTable.Item[] items) =>
        new(false, 0, 0, 24, salePrice * 2, salePrice, red, buyLimit, reset, items);

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(BonusItemId));
        test.SetTable(new PremiumClubTable(new Dictionary<int, PremiumClubTable.Buff>(), new Dictionary<int, PremiumClubTable.Item>(),
            new Dictionary<int, PremiumClubTable.Package> {
                [FreeId] = Package(300),
                [LimitedId] = Package(100, buyLimit: 2, reset: ResetType.Week, items: new PremiumClubTable.Item(BonusItemId, 3, 1, 0)),
                [RedId] = Package(50, red: true, buyLimit: 1),
                // Like KR 1101: a limited goods whose sale dates are over.
                [EndedId] = new(false, 1484226000, 1485928799, 168, 0, 0, false, 1, ResetType.Default, []),
                // A limited goods whose sale starts in a year.
                [ComingId] = new(false, DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(), 0, 24, 20, 10, false, 1, ResetType.Default, []),
                // A bonus item with no metadata: CreateItem makes nothing.
                [NoBonusId] = Package(10, items: new PremiumClubTable.Item(UnknownItemId, 1, 1, 0)),
                [RedLimitedId] = Package(40, red: true, buyLimit: 3),
                // As read from a metadata db ingested before the sale price and limit: NA 3001 (one per account).
                [OldDbId] = new(false, 0, 0, 720, 490, 0, false, null, ResetType.Default, []),
                [NoSalePriceId] = new(false, 0, 0, 24, 30, 0, false, 0, ResetType.Default, []),
                // A bonus item that lasts PeriodMinutes (bonusItemPeriod).
                [PeriodId] = Package(20, items: new PremiumClubTable.Item(BonusItemId, 2, 1, PeriodMinutes)),
            }));
        test.Start();
        // KR and NA constants.xml: 60.
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { VIPServicePeriodLimitDay = LimitDays });
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [],
                [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Config = new ConfigManager(db, session);
        }
        session.Survival = new SurvivalManager(session);
        session.Currency.Meret = 1000;
        session.Currency.GameMeret = 500;
        session.SessionSave();
        test.TakeSent();
        Assume.That(test.Errors, Is.Empty);
    }

    private PremiumClubHandler Handler() => new() { ItemMetadata = null!, TableMetadata = test.TableMetadata };

    private void Load() => Handler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

    private void Buy(int goodsId) => Handler().Handle(session, TestGameSession.Packet(writer => {
        writer.WriteByte(4);
        writer.WriteInt(goodsId);
    }));

    private List<byte[]> Sent(byte mode) => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.PremiumClub && packet[2] == mode)
        .ToList();

    private static List<(int GoodsId, int Remaining)> ReadList(byte[] packet) {
        var reader = new ByteReader(packet, 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        int count = reader.ReadInt();
        var list = new List<(int, int)>();
        for (int i = 0; i < count; i++) {
            list.Add((reader.ReadInt(), reader.ReadInt()));
        }
        return list;
    }

    private Account StoredAccount() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAccount(test.AccountId)!;
    }

    private (long Meret, long GameMeret) StoredMerets() {
        using var context = new Ms2Context(test.Options);
        var currency = context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).Currency;
        return (currency.Meret, currency.GameMeret);
    }

    private List<(int Amount, long ExpiryTime)> StoredBonusItems() {
        using var context = new Ms2Context(test.Options);
        return context.Item.AsNoTracking()
            .Where(row => row.OwnerId == session.CharacterId && row.ItemId == BonusItemId)
            .AsEnumerable()
            .Select(row => (row.Amount, row.ExpiryTime.ToEpochSeconds()))
            .ToList();
    }

    private int BagAmount() => session.Item.Inventory.Find(BonusItemId).Sum(item => item.Amount);

    [Test]
    public void LoadPackagesBytes() {
        Assert.That(PremiumCubPacket.LoadPackages([(LimitedId, 2), (RedId, 0)]).ToArray(), Is.EqualTo(new byte[] {
            0xeb, 0x00, 0x03, 0x02, 0x00, 0x00, 0x00,
            0xd1, 0x07, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
            0xd3, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        }));
    }

    [Test]
    public void PurchasePackageBytes() {
        Assert.That(PremiumCubPacket.PurchasePackage(LimitedId).ToArray(), Is.EqualTo(new byte[] { 0xeb, 0x00, 0x04, 0xd1, 0x07, 0x00, 0x00 }));
    }

    // The limited goods are listed in id order with every purchase still left: one not yet on sale too (the dialog
    // shows it greyed), not one whose sale is over.
    [Test]
    public void Load_ListsTheLimitedGoods() {
        Load();

        List<byte[]> sent = Sent(3);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(ReadList(sent[0]), Is.EqualTo(new[] { (LimitedId, 2), (RedId, 1), (ComingId, 1), (RedLimitedId, 3) }));
    }

    // A purchase charges the sale price, stores the count, the merets and the bonus items together, and answers with
    // the goods id and the new list.
    [Test]
    public void Purchase_StoresTheCountWithTheCharge() {
        Buy(LimitedId);

        List<byte[]> sent = test.TakeSent();
        List<byte[]> bought = sent.Where(packet => TestGameSession.Op(packet) == SendOp.PremiumClub && packet[2] == 4).ToList();
        Assert.That(bought, Has.Count.EqualTo(1));
        Assert.That(bought[0].Take(7), Is.EqualTo(PremiumCubPacket.PurchasePackage(LimitedId).ToArray()));
        List<byte[]> lists = sent.Where(packet => TestGameSession.Op(packet) == SendOp.PremiumClub && packet[2] == 3).ToList();
        Assert.That(ReadList(lists.Single()), Is.EqualTo(new[] { (LimitedId, 1), (RedId, 1), (ComingId, 1), (RedLimitedId, 3) }));
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(900));
            Assert.That(StoredMerets(), Is.EqualTo((900L, 500L)));
            VipGoodsPurchase stored = StoredAccount().VipGoodsPurchases.Single();
            Assert.That((stored.GoodsId, stored.Count), Is.EqualTo((LimitedId, 1)));
            Assert.That(BagAmount(), Is.EqualTo(3));
            Assert.That(StoredAccount().PremiumTime, Is.GreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // At the limit the goods is refused: nothing is charged or stored, the list is sent again.
    [Test]
    public void Purchase_AtTheLimit_ChangesNothing() {
        Buy(LimitedId);
        Buy(LimitedId);
        test.TakeSent();

        Buy(LimitedId);

        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.PremiumClub).ToList();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(ReadList(sent[0]), Is.EqualTo(new[] { (LimitedId, 0), (RedId, 1), (ComingId, 1), (RedLimitedId, 3) }));
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(800));
            Assert.That(StoredMerets(), Is.EqualTo((800L, 500L)));
            Assert.That(StoredAccount().VipGoodsPurchases.Single().Count, Is.EqualTo(2));
            Assert.That(BagAmount(), Is.EqualTo(6));
        });
    }

    // Goods without a limit keep no count and are not listed.
    [Test]
    public void Purchase_NoLimit_KeepsNoCount() {
        Buy(FreeId);
        Buy(FreeId);
        Buy(FreeId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Has.Count.EqualTo(3));
            Assert.That(session.Currency.Meret, Is.EqualTo(100));
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
        });
    }

    // priceType 1 goods are paid in red merets.
    [Test]
    public void Purchase_RedMerets() {
        Buy(RedId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Has.Count.EqualTo(1));
            Assert.That(StoredMerets(), Is.EqualTo((1000L, 450L)));
            Assert.That(StoredAccount().VipGoodsPurchases.Single().GoodsId, Is.EqualTo(RedId));
        });
    }

    [Test]
    public void Purchase_SaleEnded_ChangesNothing() {
        long premiumTime = StoredAccount().PremiumTime;

        Buy(EndedId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.PremiumClub), Is.Empty);
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
            Assert.That(StoredAccount().PremiumTime, Is.EqualTo(premiumTime));
        });
    }

    [Test]
    public void Purchase_NotEnoughMerets_ChangesNothing() {
        session.Currency.Meret = 99;
        session.SessionSave();
        test.TakeSent();

        Buy(LimitedId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Is.Empty);
            Assert.That(session.Currency.Meret, Is.EqualTo(99));
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
            Assert.That(BagAmount(), Is.EqualTo(0));
        });
    }

    // The store is refused: the session is put back, nothing is given and nothing is answered.
    [Test]
    public void Purchase_StoreRefused_ChangesNothing() {
        long premiumTime = session.Player.Value.Account.PremiumTime;
        test.FailSave = _ => true;

        Buy(LimitedId);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(Sent(4), Is.Empty);
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(session.Player.Value.Account.VipGoodsPurchases, Is.Empty);
            Assert.That(session.Player.Value.Account.PremiumTime, Is.EqualTo(premiumTime));
            Assert.That(BagAmount(), Is.EqualTo(0));
            Assert.That(StoredMerets(), Is.EqualTo((1000L, 500L)));
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
            Assert.That(test.Errors, Is.Not.Empty);
        });
    }

    // Listed before its sale starts, but not sold.
    [Test]
    public void Purchase_NotYetOnSale_ChangesNothing() {
        Buy(ComingId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Is.Empty);
            Assert.That(StoredMerets(), Is.EqualTo((1000L, 500L)));
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
        });
    }

    // A bonus item that cannot be made refuses the purchase before the charge.
    [Test]
    public void Purchase_BonusItemNotMade_ChangesNothing() {
        long premiumTime = StoredAccount().PremiumTime;

        Buy(NoBonusId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Is.Empty);
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(StoredMerets(), Is.EqualTo((1000L, 500L)));
            Assert.That(StoredAccount().PremiumTime, Is.EqualTo(premiumTime));
        });
    }

    // The charge is given back, not the balance from before: merets gained while the store ran are kept.
    [Test]
    public void Purchase_StoreRefused_KeepsAGainMeanwhile() {
        test.FailSave = _ => {
            session.Currency.ChangeMeret(7);
            return true;
        };

        Buy(LimitedId);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(Sent(4), Is.Empty);
            Assert.That(session.Currency.Meret, Is.EqualTo(1007));
            Assert.That(session.Player.Value.Account.VipGoodsPurchases, Is.Empty);
        });
    }

    [Test]
    public void Purchase_RedStoreRefused_KeepsAGainMeanwhile() {
        test.FailSave = _ => {
            session.Currency.ChangeGameMeret(5);
            return true;
        };

        Buy(RedLimitedId);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(Sent(4), Is.Empty);
            Assert.That(session.Currency.GameMeret, Is.EqualTo(505));
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        });
    }

    // Not enough red merets: the take is refused, with the red meret notice.
    [Test]
    public void Purchase_NotEnoughRedMerets_ChangesNothing() {
        session.Currency.GameMeret = 39;
        session.SessionSave();
        test.TakeSent();

        Buy(RedLimitedId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Is.Empty);
            Assert.That(session.Currency.GameMeret, Is.EqualTo(39));
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
        });
    }

    // A metadata db ingested before the limit: nothing is sold (not for 0 merets, not without the limit).
    [Test]
    public void Purchase_OldMetadataDb_ChangesNothing() {
        long premiumTime = StoredAccount().PremiumTime;

        Buy(OldDbId);
        Buy(OldDbId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.PremiumClub), Is.Empty);
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(StoredMerets(), Is.EqualTo((1000L, 500L)));
            Assert.That(StoredAccount().PremiumTime, Is.EqualTo(premiumTime));
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
        });
    }

    // The old db's goods is not listed in mode 3 either.
    [Test]
    public void Load_OldMetadataDb_NotListed() {
        Load();

        Assert.That(ReadList(Sent(3).Single()).Select(entry => entry.GoodsId), Has.No.Member(OldDbId));
    }

    // A goods without a sale price charges its price, as before; it is not given for free.
    [Test]
    public void Purchase_NoSalePrice_ChargesThePrice() {
        Buy(NoSalePriceId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Has.Count.EqualTo(1));
            Assert.That(StoredMerets(), Is.EqualTo((970L, 500L)));
        });
    }

    // A VIP whose time ends more than VIPServicePeriodLimitDay days away cannot extend it: the dialog refuses it
    // (OnClickBuyGoods), and so does the server for a packet sent anyway. Nothing is charged, sent or stored.
    [Test]
    public void Purchase_VipOverTheWindow_ChangesNothing() {
        long premiumTime = DateTime.Now.AddDays(LimitDays).AddMinutes(5).ToEpochSeconds();
        session.Player.Value.Account.PremiumTime = premiumTime;

        Buy(FreeId);
        Buy(LimitedId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.PremiumClub), Is.Empty);
            Assert.That(session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(StoredMerets(), Is.EqualTo((1000L, 500L)));
            Assert.That(session.Player.Value.Account.PremiumTime, Is.EqualTo(premiumTime));
            Assert.That(StoredAccount().VipGoodsPurchases, Is.Empty);
            Assert.That(BagAmount(), Is.EqualTo(0));
        });
    }

    // Within the window the VIP time is extended from its end.
    [Test]
    public void Purchase_VipWithinTheWindow_Extends() {
        long premiumTime = DateTime.Now.AddDays(LimitDays).AddMinutes(-5).ToEpochSeconds();
        session.Player.Value.Account.PremiumTime = premiumTime;

        Buy(FreeId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Has.Count.EqualTo(1));
            Assert.That(StoredMerets(), Is.EqualTo((700L, 500L)));
            Assert.That(StoredAccount().PremiumTime, Is.EqualTo(premiumTime + 24 * 60 * 60));
        });
    }

    // A server-table db ingested before VIPServicePeriodLimitDay (or a locale with no row) reads 0: the window is not
    // checked, so an active VIP can still buy.
    [Test]
    public void Purchase_NoLimitConstant_NotLimited() {
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { VIPServicePeriodLimitDay = 0 });
        long premiumTime = DateTime.Now.AddDays(10).ToEpochSeconds();
        session.Player.Value.Account.PremiumTime = premiumTime;

        Buy(FreeId);

        Assert.Multiple(() => {
            Assert.That(Sent(4), Has.Count.EqualTo(1));
            Assert.That(StoredMerets(), Is.EqualTo((700L, 500L)));
            Assert.That(StoredAccount().PremiumTime, Is.EqualTo(premiumTime + 24 * 60 * 60));
        });
    }

    // A VIP time that has ended is not limited.
    [Test]
    public void Purchase_VipEnded_NotLimited() {
        session.Player.Value.Account.PremiumTime = DateTime.Now.AddDays(-1).ToEpochSeconds();

        Buy(FreeId);

        Assert.That(Sent(4), Has.Count.EqualTo(1));
    }

    // bonusItemPeriod is minutes from the purchase (the bill shows CTimeSpan(period * 60)); a bonus item without one
    // keeps its metadata life (none here).
    [Test]
    public void Purchase_BonusItemPeriod_SetsTheExpiry() {
        long before = DateTime.Now.ToEpochSeconds();
        Buy(PeriodId);
        Buy(LimitedId);
        long after = DateTime.Now.ToEpochSeconds();

        List<(int Amount, long ExpiryTime)> stored = StoredBonusItems();
        List<Item> bag = session.Item.Inventory.Find(BonusItemId).ToList();
        Assert.Multiple(() => {
            Assert.That(Sent(4), Has.Count.EqualTo(2));
            Assert.That(stored.OrderBy(row => row.Amount).Select(row => row.Amount), Is.EqualTo(new[] { 2, 3 }));
            (int _, long expiry) = stored.Single(row => row.Amount == 2);
            Assert.That(expiry, Is.InRange(before + PeriodMinutes * 60, after + PeriodMinutes * 60));
            Assert.That(stored.Single(row => row.Amount == 3).ExpiryTime, Is.LessThanOrEqualTo(0));
            // The two do not stack: their expiry differs.
            Assert.That(bag.Select(item => item.Amount).OrderBy(amount => amount), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(bag.Single(item => item.Amount == 2).ExpiryTime, Is.EqualTo(expiry));
        });
    }
}

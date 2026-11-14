using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Validators;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A payback product's buy through MeretMarket 30: the handler stores the counted month with the purchase, and pays
// an earlier month before it takes the merets (a month it cannot pay refuses the buy). The handler reads the clock,
// so the payback period is set around now.
public class MeretMarketPaybackTests {
    private const int Item = 11300001;
    private const int Town = 90000100;
    private const int Product = 1000200013;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private DateTime now;

    [SetUp]
    public void SetUp() {
        now = DateTime.Now;
        Assume.That(PaybackRules.IsResetWindow(PaybackRulesTests.Kr, PaybackRulesTests.Constants(settleMinutes: 10), now), Is.False);
        DateTime start = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Item, EquipSlot.CP, slotMax: 1));
        test.SetTable(PaybackRulesTests.Kr);
        test.AddEvent(910300, GameEventType.FinishPayback, new FinishPayback());
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            PaybackStartDate = start.Year * 10000 + start.Month * 100 + 1,
            PaybackEndDate = now.AddDays(60).Date,
            PaybackSettleMinutes = 5,
            PaybackMarketProductSnList = Product,
            PaybackMailId = 50000020,
            PaybackMailPeriodDay = 90,
        });
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Player.Value.Account.PrestigeLevel = 4;
        session.Currency.Meret = 5000;
        SetPremium(700);
        test.TakeSent();
    }

    private void SetPremium(long price) {
        var entry = new MeretMarketItemMetadata(Product, 0, 40310, string.Empty, MeretMarketBannerTag.None, Item, 1, 1, 0, 0,
            MeretMarketItemSaleTag.None, MeretMarketCurrencyType.Meret, price, price, 0, 0, (JobFilterFlag) ~0,
            false, 0, 0, 0, 0, false, false, false, string.Empty, 0, 0);
        object server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var table = new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata> { [Product] = entry });
        typeof(Maple2.Server.Game.GameServer).GetField("premiumMarketCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, Maple2.Server.Game.GameServer.PremiumMarket(table, id => session.ItemMetadata.TryGet(id, out ItemMetadata? item) ? item : null));
    }

    private byte Buy() {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata, BanWordStorage = null! }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(30);
                writer.WriteByte(0);
                writer.WriteInt(Product);
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteInt(1);
                writer.WriteInt(0);
                writer.WriteBool(false);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteLong(1);
                writer.WriteBool(false);
                writer.WriteByte(3);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
            }));
        return test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket)[2];
    }

    private PaybackRecord? Stored() {
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.PaybackMonth? month = context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).Payback;
        return month == null ? null : new PaybackRecord(month.Month, month.Spent, month.StartLevel);
    }

    private int MailCount() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Count(mail => mail.ReceiverId == test.CharacterId);
    }

    [Test]
    public void Buy_StoresTheCountedMonth() {
        Assert.That(Buy(), Is.EqualTo(0x1E));
        var expected = new PaybackRecord(PaybackRules.MonthKey(now), 700, 4);
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(5000 - 700));
            Assert.That(session.Player.Value.Account.Payback, Is.EqualTo(expected));
            Assert.That(Stored(), Is.EqualTo(expected));
        });
    }

    // Last month's record: paid before the buy; while its save fails the buy is refused and nothing is taken.
    [Test]
    public void Buy_PaysTheEarlierMonthFirst() {
        session.Player.Value.Account.Payback = new PaybackRecord(PaybackRules.MonthKey(now.AddMonths(-1)), 1000, 0);
        session.Player.Value.Account.PrestigeLevel = 40;
        test.FailSave = _ => true;
        Assert.That(Buy(), Is.EqualTo(0x1F));
        test.FailSave = null;
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(5000));
            Assert.That(MailCount(), Is.EqualTo(0));
        });

        Assert.That(Buy(), Is.EqualTo(0x1E));
        var expected = new PaybackRecord(PaybackRules.MonthKey(now), 700, 40);
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(5000 - 700));
            Assert.That(MailCount(), Is.EqualTo(1));
            Assert.That(Stored(), Is.EqualTo(expected));
        });
    }
}

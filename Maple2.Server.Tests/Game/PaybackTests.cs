using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Validators;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The merat payback on a session: a payback buy counts in the account's month, and the month after mails its share
// (PaybackMailId 50000020, the KR mail) and starts the new month's record. KR rows, a period of October to December
// 2026 (the KR period, January to February 2022, is over).
public class PaybackTests {
    private const int Product = 1000200013;
    private TestGameSession test = null!;

    private static readonly DateTime October = new(2026, 10, 15, 12, 0, 0);
    private static readonly DateTime November = new(2026, 11, 2, 12, 0, 0);

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(PaybackRulesTests.Kr);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            PaybackStartDate = 20261001,
            PaybackEndDate = new DateTime(2026, 12, 31),
            PaybackSettleMinutes = 5,
            PaybackMarketProductSnList = Product,
            PaybackMailId = 50000020,
            PaybackMailPeriodDay = 90,
            PaybackMaxRewardMerat = 10000,
        });
        test.Start();
        test.Session.Player.Value.Account.PrestigeLevel = 4;
    }

    private Account Account => test.Session.Player.Value.Account;

    private List<Maple2.Database.Model.Mail> Mails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().Where(mail => mail.ReceiverId == test.CharacterId).ToList();
    }

    private PaybackRecord? Stored() {
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.PaybackMonth? month = context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).Payback;
        return month == null ? null : new PaybackRecord(month.Month, month.Spent, month.StartLevel);
    }

    [Test]
    public void BuyOfTheProduct_IsAPaybackBuy() {
        Assert.Multiple(() => {
            Assert.That(PaybackService.IsPaybackBuy(test.Session, Product, 0, MeretMarketCurrencyType.Meret, October), Is.True);
            Assert.That(PaybackService.IsPaybackBuy(test.Session, 100, Product, MeretMarketCurrencyType.RedMeret, October), Is.True);
            Assert.That(PaybackService.IsPaybackBuy(test.Session, Product, 0, MeretMarketCurrencyType.Meso, October), Is.False);
            Assert.That(PaybackService.IsPaybackBuy(test.Session, 100, 0, MeretMarketCurrencyType.Meret, October), Is.False);
            Assert.That(PaybackService.IsPaybackBuy(test.Session, Product, 0, MeretMarketCurrencyType.Meret, new DateTime(2027, 1, 2)), Is.False);
            Assert.That(PaybackService.BuyRefused(test.Session, new DateTime(2026, 11, 1, 0, 3, 0)), Is.True);
            Assert.That(PaybackService.BuyRefused(test.Session, November), Is.False);
        });
    }

    // Two buys in October: one record, its start level the level at the first.
    [Test]
    public void Spend_CountsInTheMonth() {
        PaybackService.Spend(test.Session, 600, October);
        Account.PrestigeLevel = 6;
        PaybackService.Spend(test.Session, 400, October.AddDays(1));
        Assert.That(Account.Payback, Is.EqualTo(new PaybackRecord(202610, 1000, 4)));
    }

    // October's 1000 merets with 9 levels gained (4 to 13: levels 5, 7, 9 and 12, 30%): 300 merets mailed in
    // November, once; November's record starts at level 13.
    [Test]
    public void Settle_MailsTheShareOnce() {
        PaybackService.Spend(test.Session, 1000, October);
        Account.PrestigeLevel = 13;

        Assert.That(PaybackService.Settle(test.Session, November), Is.True);
        Assert.That(PaybackService.Settle(test.Session, November.AddHours(1)), Is.False);

        List<Maple2.Database.Model.Mail> mails = Mails();
        Assert.Multiple(() => {
            Assert.That(mails, Has.Count.EqualTo(1));
            Assert.That(mails[0].Type, Is.EqualTo(MailType.System));
            Assert.That(mails[0].Content, Is.EqualTo("50000020"));
            Assert.That(mails[0].Currency.Meret, Is.EqualTo(300));
            Assert.That(mails[0].TitleArgs, Is.EqualTo(new[] { "str=10" }).AsCollection);
            Assert.That(mails[0].ContentArgs, Is.EqualTo(new[] { "str=10", "str=300", "str=30", "str=9", "str=1000" }).AsCollection);
            Assert.That(mails[0].ExpiryTime.ToEpochSeconds(), Is.EqualTo(new DateTimeOffset(November).AddDays(90).ToUnixTimeSeconds()));
            Assert.That(Account.Payback, Is.EqualTo(new PaybackRecord(202611, 0, 13)));
            Assert.That(Stored(), Is.EqualTo(new PaybackRecord(202611, 0, 13)));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // No level gained, or nothing spent: no mail, the record rolls over.
    [TestCase(1000, 4)]
    [TestCase(0, 28)]
    public void Settle_NothingDue_NoMail(long spent, int level) {
        PaybackService.Spend(test.Session, spent, October);
        if (spent == 0) {
            Assert.That(PaybackService.Settle(test.Session, October), Is.True); // October's record, stored
        }
        Account.PrestigeLevel = level;
        Assert.That(PaybackService.Settle(test.Session, November), Is.True);
        Assert.Multiple(() => {
            Assert.That(Mails(), Is.Empty);
            Assert.That(Stored(), Is.EqualTo(new PaybackRecord(202611, 0, level)));
        });
    }

    // December's record is paid in January, after the period: the record is cleared.
    [Test]
    public void Settle_AfterThePeriod_PaysAndClears() {
        PaybackService.Spend(test.Session, 300000, new DateTime(2026, 12, 20));
        Account.PrestigeLevel = 40;
        Assert.That(PaybackService.Settle(test.Session, new DateTime(2027, 1, 3)), Is.True);
        Assert.Multiple(() => {
            Assert.That(Mails().Single().Currency.Meret, Is.EqualTo(10000));
            Assert.That(Account.Payback, Is.Null);
            Assert.That(Stored(), Is.Null);
        });
    }

    // A buy in a later month pays the earlier month first.
    [Test]
    public void Spend_InALaterMonth_PaysTheEarlierFirst() {
        PaybackService.Spend(test.Session, 1000, October);
        Account.PrestigeLevel = 32;
        PaybackService.Spend(test.Session, 200, November);
        Assert.Multiple(() => {
            Assert.That(Mails().Single().Currency.Meret, Is.EqualTo(1000));
            Assert.That(Account.Payback, Is.EqualTo(new PaybackRecord(202611, 200, 32)));
        });
    }

    [Test]
    public void Packet_Status() {
        Assert.That(Convert.ToHexString(PaybackPacket.Status(9, 1000).ToArray()), Is.EqualTo("2D01" + "00" + "09000000" + "E8030000"));
        Assert.That(Convert.ToHexString(PaybackPacket.Status(0, 5_000_000_000).ToArray()), Is.EqualTo("2D01" + "00" + "00000000" + "FFFFFF7F"));
    }

    [Test]
    public void Packet_NoProducts() {
        Assert.That(Convert.ToHexString(PaybackPacket.Products([]).ToArray()), Is.EqualTo("2D01" + "01" + "00000000"));
    }
}

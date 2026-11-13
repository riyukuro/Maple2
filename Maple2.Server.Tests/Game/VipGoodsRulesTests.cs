using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// The VIP goods purchase limit windows (vipgoodstable.xml buyLimitResetType): the day starts at midnight, the week
// at midnight on Friday, the month on the 1st; reset type 0 never starts again.
public class VipGoodsRulesTests {
    private const int GoodsId = 2001;

    private static long Epoch(DateTime time) => new DateTimeOffset(time).ToUnixTimeSeconds();

    [TestCase(ResetType.Day, "2026-11-13 15:00", "2026-11-13 00:00")]
    [TestCase(ResetType.Week, "2026-11-13 15:00", "2026-11-13 00:00")] // a Friday
    [TestCase(ResetType.Week, "2026-11-12 15:00", "2026-11-06 00:00")] // a Thursday
    [TestCase(ResetType.Week, "2026-11-15 01:00", "2026-11-13 00:00")] // a Sunday
    [TestCase(ResetType.Month, "2026-11-13 15:00", "2026-11-01 00:00")]
    public void WindowStart(ResetType type, string now, string start) {
        Assert.That(VipGoodsRules.WindowStart(type, DateTime.Parse(now)), Is.EqualTo(DateTime.Parse(start)));
    }

    [Test]
    public void WindowStart_NoReset() {
        Assert.That(VipGoodsRules.WindowStart(ResetType.Default, DateTime.Now), Is.EqualTo(DateTime.MinValue));
    }

    [Test]
    public void Remaining_NoPurchase_IsTheLimit() {
        Assert.That(VipGoodsRules.Remaining([], GoodsId, 3, ResetType.Week, DateTime.Now), Is.EqualTo(3));
    }

    [Test]
    public void Record_CountsInTheWindow() {
        var purchases = new List<VipGoodsPurchase>();
        DateTime now = DateTime.Parse("2026-11-13 15:00");

        VipGoodsRules.Record(purchases, GoodsId, ResetType.Week, now);
        VipGoodsRules.Record(purchases, GoodsId, ResetType.Week, now.AddDays(2));

        Assert.That(purchases.Single().Count, Is.EqualTo(2));
        Assert.That(purchases.Single().LastPurchase, Is.EqualTo(Epoch(now.AddDays(2))));
        Assert.That(VipGoodsRules.Remaining(purchases, GoodsId, 2, ResetType.Week, now.AddDays(2)), Is.EqualTo(0));
        Assert.That(VipGoodsRules.Remaining(purchases, GoodsId, 3, ResetType.Week, now.AddDays(2)), Is.EqualTo(1));
    }

    // A count of an earlier window is not counted, and the next purchase starts it again.
    [Test]
    public void Record_NewWindow_StartsAgain() {
        var purchases = new List<VipGoodsPurchase> {
            new() { GoodsId = GoodsId, Count = 2, LastPurchase = Epoch(DateTime.Parse("2026-11-12 23:59")) },
        };
        DateTime friday = DateTime.Parse("2026-11-13 00:01");

        Assert.That(VipGoodsRules.Remaining(purchases, GoodsId, 2, ResetType.Week, friday), Is.EqualTo(2));
        VipGoodsRules.Record(purchases, GoodsId, ResetType.Week, friday);
        Assert.That(purchases.Single().Count, Is.EqualTo(1));
    }

    [Test]
    public void NoReset_CountsForever() {
        var purchases = new List<VipGoodsPurchase> {
            new() { GoodsId = GoodsId, Count = 1, LastPurchase = Epoch(DateTime.Parse("2020-01-01")) },
        };

        Assert.That(VipGoodsRules.Remaining(purchases, GoodsId, 1, ResetType.Default, DateTime.Now), Is.EqualTo(0));
    }

    // Other goods' purchases do not count.
    [Test]
    public void OtherGoods_NotCounted() {
        var purchases = new List<VipGoodsPurchase>();
        VipGoodsRules.Record(purchases, GoodsId + 1, ResetType.Day, DateTime.Now);

        Assert.That(VipGoodsRules.Remaining(purchases, GoodsId, 1, ResetType.Day, DateTime.Now), Is.EqualTo(1));
    }

    // The week starts at midnight on Friday as the dialog's tooltip says, not on the shops' reset day (Thursday): a
    // limit-1 goods bought on Thursday night can be bought again just after midnight.
    [Test]
    public void Week_StartsFriday_NotResetDay() {
        Assert.That(Constant.ResetDay, Is.Not.EqualTo(DayOfWeek.Friday));
        var purchases = new List<VipGoodsPurchase>();
        VipGoodsRules.Record(purchases, GoodsId, ResetType.Week, DateTime.Parse("2026-11-12 23:00"));

        Assert.That(VipGoodsRules.Remaining(purchases, GoodsId, 1, ResetType.Week, DateTime.Parse("2026-11-12 23:59")), Is.EqualTo(0));
        Assert.That(VipGoodsRules.Remaining(purchases, GoodsId, 1, ResetType.Week, DateTime.Parse("2026-11-13 00:30")), Is.EqualTo(1));
    }
}

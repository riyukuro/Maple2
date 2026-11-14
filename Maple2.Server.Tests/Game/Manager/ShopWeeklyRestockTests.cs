using System;
using System.Reflection;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game.Manager;

// A shop of the weekly restock schedule (ResetType Week, no fixed restock time) restocks at the weekly reset, 00:00 on
// Friday, as the client's weekly buy limit texts say (s_tooltip_buylimit_week_*: every Friday at 00:00).
public class ShopWeeklyRestockTests {
    [Test]
    public void WeeklyRestockIsTheNextFridayMidnight() {
        var test = new TestGameSession();
        test.Start();
        var shop = new ShopMetadata(1, 0, "", default, false, false, false, false, false, false, false, 0, true,
            new ShopRestockData(ResetType.Week, ShopCurrencyType.Meso, ShopCurrencyType.Meso, 0, 0, 0, false, false, false));
        MethodInfo restock = typeof(ShopManager).GetMethod("GetRestockTime", BindingFlags.Instance | BindingFlags.NonPublic)!;

        DateTime before = DateTime.Now;
        long time = (long) restock.Invoke(test.Session.Shop, [shop])!;
        DateTime after = DateTime.Now;

        Assert.That(time, Is.EqualTo(WeeklyResetRules.NextStart(before).ToEpochSeconds())
            .Or.EqualTo(WeeklyResetRules.NextStart(after).ToEpochSeconds()));
        DateTime local = DateTimeOffset.FromUnixTimeSeconds(time).LocalDateTime;
        Assert.That((local.DayOfWeek, local.TimeOfDay), Is.EqualTo((DayOfWeek.Friday, TimeSpan.Zero)));
        Assert.That(local, Is.GreaterThan(before).And.LessThanOrEqualTo(before.AddDays(7)));
    }
}

using System;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game;

// A timed item is past its time from the second of its expiry on, as the client counts it (CItem::IsExpired).
public class ItemExpiryTests {
    [Test]
    public void AnItemIsExpiredFromItsExpirySecondOn() {
        var test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(20000001, type: 2));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.Multiple(() => {
            item.ExpiryTime = 0;
            Assert.That(item.IsExpired(), Is.False);
            item.ExpiryTime = now + 60;
            Assert.That(item.IsExpired(), Is.False);
            item.ExpiryTime = now;
            Assert.That(item.IsExpired(), Is.True);
            Assert.That(SetItemRules.Expired(now, now, false), Is.True);
        });
    }

    // The online check counts the worn items past their time; the totals are rebuilt when the count is not the
    // one they were built with.
    [Test]
    public void AWornItemRunningOut_ChangesTheCount() {
        var test = new TestGameSession();
        Item gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        Item outfit = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, type: 1));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        gear.ExpiryTime = now + 10;

        Assert.Multiple(() => {
            Assert.That(StatsManager.ExpiredWorn([gear, outfit], now), Is.Zero);
            Assert.That(StatsManager.ExpiredWorn([gear, outfit], now + 9), Is.Zero);
            Assert.That(StatsManager.ExpiredWorn([gear, outfit], now + 10), Is.EqualTo(1));
        });
    }
}

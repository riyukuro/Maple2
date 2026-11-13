using System;
using Maple2.Database.Extensions;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The mentee attend streak as the client's reward page reads it (CMentoringManager FUN_140b715c0, FUN_140b71610).
public class MentoringAttendTests {
    private static readonly DateTime Noon = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Local);

    [Test]
    public void Claim_DaysInARowGiveTheTableCoinsAndStayOnTheThird() {
        var now = Noon;
        (int coin, int level, long next) = MentoringAttend.Claim(0, 0, now);
        Assert.That((coin, level), Is.EqualTo((100, 1)));
        Assert.That(next, Is.EqualTo(new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Local).ToEpochSeconds()));

        int[] coins = new int[4];
        for (int day = 0; day < coins.Length; day++) {
            now = now.AddDays(1);
            Assert.That(MentoringAttend.Available(next, now.ToEpochSeconds()), Is.True);
            (coins[day], level, next) = MentoringAttend.Claim(level, next, now);
        }
        Assert.That(coins, Is.EqualTo(new[] { 200, 300, 300, 300 }));
    }

    [Test]
    public void Claim_AMissedDayStartsAgain() {
        (_, int level, long next) = MentoringAttend.Claim(0, 0, Noon);
        (_, level, next) = MentoringAttend.Claim(level, next, Noon.AddDays(1));
        Assert.That(level, Is.EqualTo(2));

        // The next claim time is midnight; a whole day after it the client shows level 0.
        DateTime late = next.FromEpochSeconds().AddDays(1);
        Assert.That(MentoringAttend.Level(level, next, late.AddSeconds(-1).ToEpochSeconds()), Is.EqualTo(2));
        Assert.That(MentoringAttend.Level(level, next, late.ToEpochSeconds()), Is.EqualTo(0));
        Assert.That(MentoringAttend.Claim(level, next, late).Coin, Is.EqualTo(100));
    }

    [Test]
    public void Available_FromTheNextClaimTime() {
        (_, _, long next) = MentoringAttend.Claim(0, 0, Noon);
        Assert.That(MentoringAttend.Available(next, next - 1), Is.False);
        Assert.That(MentoringAttend.Available(next, next), Is.True);
    }
}

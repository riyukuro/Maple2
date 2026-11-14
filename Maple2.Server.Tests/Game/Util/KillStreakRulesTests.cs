using Maple2.Model.Enum;
using GameLua = Maple2.Server.Game.LuaFunctions.Lua;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class KillStreakRulesTests {
    [Test]
    public void FirstKillStartsAtOne() {
        Assert.That(KillStreakRules.Next(0, 20, 20, long.MaxValue), Is.EqualTo(1));
        Assert.That(KillStreakRules.Next(0, 20, 20, 0), Is.EqualTo(1));
    }

    // KMS2: the longest gap that kept a streak was 4.97 s, the shortest that broke one 5.98 s; the client's Lua has 5000.
    [Test]
    public void KillWithinFiveSecondsAddsOne() {
        Assert.That(KillStreakRules.Next(7, 20, 20, 4970), Is.EqualTo(8));
        Assert.That(KillStreakRules.Next(7, 20, 20, 4999), Is.EqualTo(8));
        Assert.That(KillStreakRules.Next(7, 20, 20, 5000), Is.EqualTo(1));
        Assert.That(KillStreakRules.Next(7, 20, 20, 5980), Is.EqualTo(1));
        Assert.That(KillStreakRules.Next(7, 20, 20, long.MaxValue), Is.EqualTo(1));
    }

    // The client's calcKillCount: a target more than 5 levels away and below level 50 leaves the count as it is.
    [Test]
    public void FarLevelTargetKeepsCount() {
        Assert.That(GameLua.CalcKillCount(20, 25, 1000, 7), Is.EqualTo(8));
        Assert.That(GameLua.CalcKillCount(20, 14, 1000, 7), Is.EqualTo(7));
        Assert.That(GameLua.CalcKillCount(20, 26, 1000, 7), Is.EqualTo(7));
        Assert.That(GameLua.CalcKillCount(70, 50, 1000, 7), Is.EqualTo(8));
        Assert.That(GameLua.CalcKillCount(20, 14, 5000, 7), Is.EqualTo(1));
    }

    // The client's calcKillCountBonusExpRate. KMS2 paid base 18007 kills 18187 (+1%) at 21 kills, 18727 (+4%) at 31,
    // 19267 (+7%) at 41, 19807 (+10%) at 51 and 20708 (+15%) at 101.
    [TestCase(20, 0)]
    [TestCase(21, 0.01)]
    [TestCase(30, 0.01)]
    [TestCase(31, 0.04)]
    [TestCase(41, 0.07)]
    [TestCase(51, 0.1)]
    [TestCase(100, 0.1)]
    [TestCase(101, 0.15)]
    public void StreakBonusRate(int streak, double rate) {
        Assert.That(KillStreakRules.BonusRate(streak), Is.EqualTo(rate));
    }

    // The kill that makes the streak 21 already pays the 1%: the streak counts the kill before its exp, and the bonus
    // adds to the stat rate.
    [Test]
    public void KillExpRate_AddsTheStreakBonus() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.Value.Character.Level = 50;
        for (int kill = 1; kill <= 20; kill++) {
            session.Exp.OnKill(50, kill * 1000L);
        }
        Assert.That(session.Exp.KillExpRate(0.1), Is.EqualTo(0.1));

        session.Exp.OnKill(50, 21_000L);
        Assert.That(session.Exp.KillExpRate(0.1), Is.EqualTo(0.11).Within(1e-12));
        Assert.That(KillRateOf(18007, session.Exp.KillExpRate(0)), Is.EqualTo(18187));

        session.Exp.OnKill(50, 27_000L);
        Assert.That(session.Exp.KillExpRate(0.1), Is.EqualTo(0.1));
    }

    private static long KillRateOf(long exp, double rate) => Maple2.Server.Game.Util.KillRate.Apply(exp, rate);

    // The trophy keeps the longest streak: a streak at or below it sends nothing.
    [Test]
    public void TrophyKeepsLongestStreak() {
        Assert.That(AchievementManager.Advances(ConditionType.killcount, 9, 9), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.killcount, 9, 3), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.killcount, 9, 10), Is.True);
    }
}

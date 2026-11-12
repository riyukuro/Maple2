using Maple2.Model.Enum;
using GameLua = Maple2.Server.Game.LuaFunctions.Lua;
using Maple2.Server.Game.Manager;
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

    // The trophy keeps the longest streak: a streak at or below it sends nothing.
    [Test]
    public void TrophyKeepsLongestStreak() {
        Assert.That(AchievementManager.Advances(ConditionType.killcount, 9, 9), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.killcount, 9, 3), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.killcount, 9, 10), Is.True);
    }
}

using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// After a revival the client shows the player invincible for UserRevivalInvincibleTick; the server must not let
// monsters kill the player again inside that window, and a dead player takes no hits at all.
public class RevivalInvincibleTests {
    [Test]
    public void DeadPlayerTakesNoDamage() {
        Assert.That(FieldPlayer.TakesDamage(true, 10_000, 0), Is.False);
    }

    [Test]
    public void InsideTheWindowNoDamage() {
        Assert.That(FieldPlayer.TakesDamage(false, 10_000, 14_000), Is.False);
    }

    [TestCase(14_000)]
    [TestCase(20_000)]
    public void AfterTheWindowDamage(long tick) {
        Assert.That(FieldPlayer.TakesDamage(false, tick, 14_000), Is.True);
    }

    [Test]
    public void NeverRevivedDamage() {
        Assert.That(FieldPlayer.TakesDamage(false, 1, 0), Is.True);
    }
}

using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// A gathered object's respawn is each character's own.
public class GatherRespawnTests {
    private const int ResetTime = 1000;

    [Test]
    public void FastRepeatIsRefusedUntilTheResetTime() {
        var respawn = new GatherRespawn();
        Assert.That(respawn.Take(1, 5000, ResetTime), Is.True);
        Assert.That(respawn.Take(1, 5001, ResetTime), Is.False);
        Assert.That(respawn.Take(1, 5999, ResetTime), Is.False);
        Assert.That(respawn.Take(1, 6000, ResetTime), Is.True);
    }

    [Test]
    public void RefusedTryDoesNotMoveTheRespawn() {
        var respawn = new GatherRespawn();
        Assert.That(respawn.Take(1, 5000, ResetTime), Is.True);
        Assert.That(respawn.Take(1, 5900, ResetTime), Is.False);
        Assert.That(respawn.Take(1, 6000, ResetTime), Is.True);
    }

    [Test]
    public void SecondPlayerGathersWhileTheFirstWaits() {
        var respawn = new GatherRespawn();
        Assert.That(respawn.Take(1, 5000, ResetTime), Is.True);
        Assert.That(respawn.Take(2, 5000, ResetTime), Is.True);
        Assert.That(respawn.Take(2, 5500, ResetTime), Is.False);
        Assert.That(respawn.Take(1, 6000, ResetTime), Is.True);
        Assert.That(respawn.Take(2, 6000, ResetTime), Is.True);
    }

    [Test]
    public void NoResetTimeNeverWaits() {
        var respawn = new GatherRespawn();
        Assert.That(respawn.Take(1, 5000, 0), Is.True);
        Assert.That(respawn.Take(1, 5000, 0), Is.True);
    }

    [Test]
    public void ManyPlayersKeepTheirOwnTimes() {
        var respawn = new GatherRespawn();
        for (long id = 1; id <= 200; id++) {
            Assert.That(respawn.Take(id, 5000 + id, ResetTime), Is.True);
        }
        Assert.That(respawn.Take(200, 5300, ResetTime), Is.False);
        Assert.That(respawn.Take(1, 6001, ResetTime), Is.True);
    }
}

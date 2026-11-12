using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class FishingRulesTests {
    // Fishing 0x0073 mode 8 after the mode byte: u8 result.
    [TestCase((byte) 1, true, 20000, 15000, true)] // bite, tick passed
    [TestCase((byte) 1, true, 14500, 15000, true)] // inside the slack
    [TestCase((byte) 1, true, 10001, 15000, false)] // sent right after the start
    [TestCase((byte) 1, false, 40000, 15000, false)] // the cast had no bite
    [TestCase((byte) 0, true, 20000, 15000, false)] // the client reports a miss
    public void ResultByteOfCatch(byte wire, bool bite, long nowTick, long biteTick, bool expected) {
        var packet = new ByteReader(new byte[] { wire }, 0);
        bool success = packet.ReadBool();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(FishingRules.IsCatch(success, bite, nowTick, biteTick), Is.EqualTo(expected));
    }

    // Result 0 on a cast without the reel game gets no answer and keeps the cast; with the game it ends it.
    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    public void ResultZeroWithoutTheGameIsIgnored(bool clientSuccess, bool fightGame, bool expected) {
        Assert.That(FishingRules.IsIgnoredResult(clientSuccess, fightGame), Is.EqualTo(expected));
    }

    // A cast whose reel game was won, then the next cast: the game flag of the start answer comes from the
    // new cast only. Without a bite there is no game, so the client does not send a "great" result that
    // the server would then refuse.
    [Test]
    public void ReelGameDoesNotCarryToTheNextCast() {
        // Cast 1: bite, fight roll under the chance: game, and the won catch counts.
        Assert.That(FishingRules.IsFightGame(true, false, 100, 3000), Is.True);
        Assert.That(FishingRules.IsCatch(true, true, 20000, 15000), Is.True);
        // Cast 2: no bite, whatever the roll: no game.
        Assert.That(FishingRules.IsFightGame(false, false, 100, 3000), Is.False);
        // Cast 3: bite, roll over the chance: no game, a plain catch.
        Assert.That(FishingRules.IsFightGame(true, false, 3000, 3000), Is.False);
        Assert.That(FishingRules.IsCatch(true, true, 20000, 15000), Is.True);
        // Auto fishing never opens the game.
        Assert.That(FishingRules.IsFightGame(true, true, 100, 3000), Is.False);
    }
}

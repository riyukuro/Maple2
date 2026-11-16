using System.Linq;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Game;

// The corpse box rolls (CorpseLootRolls): once a second for each player who hits the corpse, not on every hit.
public class CorpseLootRollsTests {
    private const long Player = 20000000001;
    private const long Other = 20000000002;

    [Test]
    public void FiftyHitsInOneSecond_RollOnce() {
        var rolls = new CorpseLootRolls();
        int rolled = Enumerable.Range(0, 50).Count(hit => rolls.TryRoll(Player, 10_000 + hit * 19));
        Assert.That(rolled, Is.EqualTo(1));
    }

    [Test]
    public void HitsOneSecondApart_RollEach() {
        var rolls = new CorpseLootRolls();
        int rolled = Enumerable.Range(0, 20).Count(hit => rolls.TryRoll(Player, 10_000 + hit * CorpseLootRolls.IntervalMs));
        Assert.That(rolled, Is.EqualTo(20));
        // 999 ms after the last roll is still inside its second.
        Assert.That(rolls.TryRoll(Player, 10_000 + 19 * CorpseLootRolls.IntervalMs + 999), Is.False);
    }

    [Test]
    public void TwoPlayers_EachRollTheirOwn() {
        var rolls = new CorpseLootRolls();
        Assert.Multiple(() => {
            Assert.That(rolls.TryRoll(Player, 10_000), Is.True);
            Assert.That(rolls.TryRoll(Other, 10_001), Is.True);
            Assert.That(rolls.TryRoll(Player, 10_500), Is.False);
            Assert.That(rolls.TryRoll(Other, 10_500), Is.False);
            Assert.That(rolls.TryRoll(Player, 11_000), Is.True);
        });
    }
}

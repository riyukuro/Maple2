using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcSkillCooldownTests {
    // <skill coolDown="3000">: a cast at 10000 leaves 2000 at 11000 and nothing from 13000; no coolDown or no cast yet
    // leaves nothing.
    [TestCase(3000, 10_000L, 11_000L, 2000L)]
    [TestCase(3000, 10_000L, 13_000L, 0L)]
    [TestCase(3000, 10_000L, 20_000L, 0L)]
    [TestCase(0, 10_000L, 10_000L, 0L)]
    [TestCase(3000, null, 10_000L, 0L)]
    public void Remaining(int cooldown, long? lastCast, long tick, long expected) {
        Assert.That(NpcSkillCooldown.Remaining(cooldown, lastCast, tick), Is.EqualTo(expected));
    }
}

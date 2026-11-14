using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// ModifyEffectDuration on a held effect: its time left times the factor, plus the value in seconds.
public class ModifyEffectDurationTests {
    [Test]
    public void TheFactorScalesTheTimeLeft() {
        // 50003004 level 1 doubles and level 2 triples 50003000's time left; 50003005 halves it.
        Assert.That(Buff.ModifiedEndTick(26000, 10000, 2f, 0f), Is.EqualTo(10000 + 32000));
        Assert.That(Buff.ModifiedEndTick(26000, 10000, 3f, 0f), Is.EqualTo(10000 + 48000));
        Assert.That(Buff.ModifiedEndTick(26000, 10000, 0.5f, 0f), Is.EqualTo(10000 + 8000));
    }

    [Test]
    public void TheValueAddsSeconds() {
        // 62100150 level 2: factor 1, value 8 on 73000004 (30 s): 20 s left become 28 s.
        Assert.That(Buff.ModifiedEndTick(40000, 20000, 1f, 8f), Is.EqualTo(20000 + 28000));
    }

    [Test]
    public void TheTimeLeftIsNotBelowZero() {
        Assert.That(Buff.ModifiedEndTick(15000, 10000, 1f, -8f), Is.EqualTo(10000));
        // An end already past counts as no time left.
        Assert.That(Buff.ModifiedEndTick(9000, 10000, 2f, 0f), Is.EqualTo(10000));
    }
}

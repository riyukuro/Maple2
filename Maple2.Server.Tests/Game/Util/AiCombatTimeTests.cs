using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class AiCombatTimeTests {
    // AI_DevilHugeRed: windows 0-13000 and 13000-26000 in a loop of 52000 share their edge.
    [TestCase(0, true, false)]
    [TestCase(12999, true, false)]
    [TestCase(13000, false, true)]
    [TestCase(25999, false, true)]
    [TestCase(26000, false, false)]
    [TestCase(52000, true, false)]
    [TestCase(65000, false, true)]
    public void LoopWindowsAreHalfOpen(long time, bool first, bool second) {
        Assert.Multiple(() => {
            Assert.That(AiCombatTime.Passes(time, 0, 52000, 13000), Is.EqualTo(first));
            Assert.That(AiCombatTime.Passes(time, 13000, 52000, 26000), Is.EqualTo(second));
        });
    }

    // Begin 20000 and loop 23000 without an end: the window runs to the end of the loop.
    [TestCase(19999, false)]
    [TestCase(20000, true)]
    [TestCase(22999, true)]
    [TestCase(23000, false)]
    [TestCase(43000, true)]
    public void NoEndRunsToTheLoopEnd(long time, bool passes) {
        Assert.That(AiCombatTime.Passes(time, 20000, 23000, 0), Is.EqualTo(passes));
    }

    // setData aiBattleTime=32000 moves the battle time to 32000, so a 30000-48000 window passes at once.
    [Test]
    public void SetDataMovesTheBattleTime() {
        long fieldTick = 500000;
        long start = fieldTick - 32000;
        Assert.Multiple(() => {
            Assert.That(AiCombatTime.Passes(fieldTick - start, 30000, 9900000, 48000), Is.True);
            Assert.That(AiCombatTime.Passes(fieldTick - start, 0, 9900000, 30000), Is.False);
        });
    }

    [Test]
    public void NegativeTimeNeverPasses() {
        Assert.That(AiCombatTime.Passes(-1, 0, 52000, 13000), Is.False);
    }
}

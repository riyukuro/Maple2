using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class AiTriggerValueTests {
    // AI_SummonBaraha: +1 when a monster enters battle, -1 when it dies; the trigger waits for the count to fall to 0.
    [Test]
    public void MonsterManyCountsUpAndDown() {
        var values = new Dictionary<string, int> { ["MonsterMany"] = 0 };
        AiTriggerValue.Modify(values, "MonsterMany", 1);
        AiTriggerValue.Modify(values, "MonsterMany", 1);
        AiTriggerValue.Modify(values, "MonsterMany", -1);
        Assert.That(values["MonsterMany"], Is.EqualTo(1));
        Assert.That(AiTriggerValue.Modify(values, "MonsterMany", -1), Is.EqualTo(0));
    }

    // BridgeAppear: each of the three bosses sends +1, and the bridge comes at 3. A key not set yet starts from 0.
    [Test]
    public void MissingKeyStartsFromZero() {
        var values = new Dictionary<string, int>();
        foreach (int _ in Enumerable.Range(0, 3)) {
            AiTriggerValue.Modify(values, "BridgeAppear", 1);
        }
        Assert.Multiple(() => {
            Assert.That(values["BridgeAppear"], Is.EqualTo(3));
            Assert.That(AiTriggerValue.Modify(values, "SummonZakumArmMany", -1), Is.EqualTo(-1));
        });
    }
}

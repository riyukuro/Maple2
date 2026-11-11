using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class TriggerUserValuesTests {
    // AI_SummonBaraha: +1 when a monster enters battle, -1 when it dies; the trigger waits for the count to fall to 0.
    [Test]
    public void MonsterManyCountsUpAndDown() {
        var values = new TriggerUserValues();
        values.Set(900103, "MonsterMany", 0);
        values.Add(900103, "MonsterMany", 1);
        values.Add(900103, "MonsterMany", 1);
        values.Add(900103, "MonsterMany", -1);
        Assert.That(values.Get(900103, "MonsterMany"), Is.EqualTo(1));
        Assert.That(values.Add(900103, "MonsterMany", -1), Is.EqualTo(0));
    }

    // BridgeAppear: each of the three bosses sends +1, and the bridge comes at 3. A key not set yet starts from 0.
    [Test]
    public void MissingKeyStartsFromZero() {
        var values = new TriggerUserValues();
        foreach (int _ in Enumerable.Range(0, 3)) {
            values.Add(551, "BridgeAppear", 1);
        }
        Assert.Multiple(() => {
            Assert.That(values.Get(551, "BridgeAppear"), Is.EqualTo(3));
            Assert.That(values.Contains(551, "SummonZakumArmMany"), Is.False);
            Assert.That(values.Get(551, "SummonZakumArmMany"), Is.EqualTo(0));
            Assert.That(values.Add(551, "SummonZakumArmMany", -1), Is.EqualTo(-1));
        });
    }

    // set_user_value with trigger_id writes that trigger's value; without one, the writing trigger's own.
    [TestCase(9995000, 9995001, 9995001)]
    [TestCase(9995000, 0, 9995000)]
    public void WriteTarget(int own, int triggerId, int expected) {
        Assert.That(TriggerUserValues.Target(own, triggerId), Is.EqualTo(expected));
    }

    // 02000066: the main script sets randomTalk for trigger 9995001 (alon), and each npc script reads its own; the
    // others are not set until main names them.
    [Test]
    public void ValueGoesToTheNamedTriggerOnly() {
        var values = new TriggerUserValues();
        values.Set(TriggerUserValues.Target(1, 9995001), "randomTalk", 1);
        Assert.Multiple(() => {
            Assert.That(values.Get(9995001, "randomTalk"), Is.EqualTo(1));
            Assert.That(values.Get(9995002, "randomTalk"), Is.EqualTo(0));
            Assert.That(values.Get(1, "randomTalk"), Is.EqualTo(0));
        });
    }

    // 02000378: every mob-wave script keeps its own WaveTime; one script's count does not move another's.
    [Test]
    public void SameKeyInTwoTriggersStaysApart() {
        var values = new TriggerUserValues();
        values.Set(TriggerUserValues.Target(901, 0), "WaveTime", 2);
        values.Set(TriggerUserValues.Target(902, 0), "WaveTime", 0);
        Assert.Multiple(() => {
            Assert.That(values.Get(901, "WaveTime"), Is.EqualTo(2));
            Assert.That(values.Get(902, "WaveTime"), Is.EqualTo(0));
        });
    }
}

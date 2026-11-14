using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// Fishing, arcade, performing, gathering and crafting exp raised by their special attributes (FishingExp, ArcadeExp,
// PlayInstrumentExp, AcquireGatheringExp, AcquireManufacturingExp):
// before, Game read none of them and the exp was the curve's alone.
public class AttributeExpRateTests {
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.fishing] = new(1, 1f),
            [ExpType.arcade] = new(1, 1f),
            [ExpType.musicMastery3] = new(1, 1f),
            [ExpType.gathering] = new(1, 1f),
            [ExpType.manufacturing] = new(1, 1f),
            [ExpType.petTaming] = new(1, 1f),
        }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000173));
        session.Player.Value.Character.Level = 50;
        test.TakeSent();
        return session;
    }

    // ExpUp: op, byte, i64 gain, ...
    private long SentGain() =>
        BitConverter.ToInt64(test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.ExpUp), 3);

    // The curve gives 500 at level 50; 10000061's top level is +2.4% (24 per mille) on all three.
    [TestCase(ExpType.fishing, SpecialAttribute.FishingExp, 2f, 1024)]
    [TestCase(ExpType.arcade, SpecialAttribute.ArcadeExp, 1f, 512)]
    [TestCase(ExpType.musicMastery3, SpecialAttribute.PlayInstrumentExp, 1f, 512)]
    [TestCase(ExpType.gathering, SpecialAttribute.AcquireGatheringExp, 1f, 512)]
    [TestCase(ExpType.manufacturing, SpecialAttribute.AcquireManufacturingExp, 1f, 512)]
    public void Gain_RaisedByItsAttribute(ExpType type, SpecialAttribute attribute, float modifier, long expected) {
        GameSession session = Start();
        session.Player.Stats.Values[attribute].AddTotal(24);

        Assert.That(session.Exp.AddExp(type, modifier), Is.EqualTo(expected));
        Assert.That(SentGain(), Is.EqualTo(expected));
    }

    // Another type's attribute does not raise it.
    [Test]
    public void Gain_OnlyItsOwnAttribute() {
        GameSession session = Start();
        session.Player.Stats.Values[SpecialAttribute.FishingExp].AddTotal(500);

        Assert.That(session.Exp.AddExp(ExpType.arcade), Is.EqualTo(500));
        Assert.That(session.Exp.AddExp(ExpType.gathering), Is.EqualTo(500));
        Assert.That(session.Exp.AddExp(ExpType.petTaming), Is.EqualTo(500));
    }

    [TestCase(ExpType.fishing, SpecialAttribute.FishingExp)]
    [TestCase(ExpType.arcade, SpecialAttribute.ArcadeExp)]
    [TestCase(ExpType.musicMastery1, SpecialAttribute.PlayInstrumentExp)]
    [TestCase(ExpType.musicMastery4, SpecialAttribute.PlayInstrumentExp)]
    [TestCase(ExpType.gathering, SpecialAttribute.AcquireGatheringExp)]
    [TestCase(ExpType.manufacturing, SpecialAttribute.AcquireManufacturingExp)]
    [TestCase(ExpType.petTaming, null)]
    [TestCase(ExpType.miniGame, null)]
    public void RateAttribute(ExpType type, SpecialAttribute? expected) {
        Assert.That(CommonExpRules.RateAttribute(type), Is.EqualTo(expected));
    }

    // A rate below -100% gives nothing, never a negative amount.
    [Test]
    public void WithRate_NeverBelowZero() {
        Assert.That(CommonExpRules.WithRate(2f, 0.5), Is.EqualTo(3f));
        Assert.That(CommonExpRules.WithRate(1f, -2), Is.EqualTo(0f));
    }
}

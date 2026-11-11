using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class EnterEventFieldRulesTests {
    [TestCase(false, false, true, false, false, EnterEventFieldRules.Result.NoField)]
    [TestCase(false, true, true, false, false, EnterEventFieldRules.Result.NoField)]
    [TestCase(true, true, true, false, false, EnterEventFieldRules.Result.Dead)]
    [TestCase(true, true, false, false, false, EnterEventFieldRules.Result.Dead)]
    [TestCase(true, false, false, false, false, EnterEventFieldRules.Result.NoEvent)]
    [TestCase(true, false, true, true, false, EnterEventFieldRules.Result.SameField)]
    [TestCase(true, false, true, false, true, EnterEventFieldRules.Result.InBattle)]
    [TestCase(true, false, true, false, false, EnterEventFieldRules.Result.Move)]
    public void Decide(bool hasField, bool isDead, bool hasEvent, bool sameField, bool inBattle, EnterEventFieldRules.Result expected) {
        Assert.That(EnterEventFieldRules.Decide(hasField, isDead, hasEvent, sameField, inBattle), Is.EqualTo(expected));
    }

    // The packet has no fields.
    [Test]
    public void NoBytes() {
        var reader = new Maple2.PacketLib.Tools.ByteReader([], 0);
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

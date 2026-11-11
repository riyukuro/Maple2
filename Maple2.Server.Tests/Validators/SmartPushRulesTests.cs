using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class SmartPushRulesTests {
    // i32 id, i32 argument, i32 recipe, bool extend: 13 bytes for every push.
    [Test]
    public void Bytes() {
        var writer = new ByteWriter();
        writer.WriteInt(1002);
        writer.WriteInt(7);
        writer.WriteInt(20000031);
        writer.WriteBool(true);
        var reader = new ByteReader(writer.ToArray(), 0);
        (int id, int arg, int recipeId, bool extend) = SmartPushRules.Read(reader);
        Assert.That(id, Is.EqualTo(1002));
        Assert.That(arg, Is.EqualTo(7));
        Assert.That(recipeId, Is.EqualTo(20000031));
        Assert.That(extend, Is.True);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(1, true)]
    [TestCase(1000, true)]
    [TestCase(0, false)]
    [TestCase(-5, false)]
    [TestCase(1001, false)]
    [TestCase(int.MaxValue, false)]
    public void AutoCount(int count, bool expected) {
        Assert.That(SmartPushRules.AutoCountAllowed(count), Is.EqualTo(expected));
    }

    [Test]
    public void RequiredItem() {
        Assert.That(SmartPushRules.ParseRequiredItem(new[] { "20300078", "1", "1" }), Is.EqualTo((20300078, "", 1)));
        Assert.That(SmartPushRules.ParseRequiredItem(new[] { "20300078:AutoGathering", "1", "2" }), Is.EqualTo((20300078, "AutoGathering", 2)));
        Assert.That(SmartPushRules.ParseRequiredItem(new string[0]), Is.EqualTo((0, "", 0)));
        Assert.That(SmartPushRules.ParseRequiredItem(new[] { "x" }), Is.EqualTo((0, "", 0)));
        Assert.That(SmartPushRules.ParseRequiredItem(new[] { "5", "1", "-3" }), Is.EqualTo((5, "", 0)));
    }

    [Test]
    public void Affords() {
        Assert.That(SmartPushRules.Affords(30, 30), Is.True);
        Assert.That(SmartPushRules.Affords(29, 30), Is.False);
        Assert.That(SmartPushRules.Affords(30, -1), Is.False);
        Assert.That(SmartPushRules.Affords(0, 0), Is.True);
    }
}

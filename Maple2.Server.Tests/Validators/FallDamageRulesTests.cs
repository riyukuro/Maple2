using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class FallDamageRulesTests {
    [TestCase(1500f, true, 500f)]
    [TestCase(1500f, false, 0f)]
    [TestCase(1000f, true, 0f)]
    [TestCase(-5f, true, 0f)]
    [TestCase(float.NaN, true, 0f)]
    [TestCase(float.PositiveInfinity, true, 0f)]
    public void Distance(float height, bool fieldFallDamage, float expected) {
        Assert.That(FallDamageRules.Distance(height, fieldFallDamage), Is.EqualTo(expected));
    }

    // Out of the field's box: health is taken only in a field with fall damage.
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void OutOfBoundsHurts(bool fieldFallDamage, bool expected) {
        Assert.That(FallDamageRules.OutOfBoundsHurts(fieldFallDamage), Is.EqualTo(expected));
    }

    // The packet is one float.
    [Test]
    public void Bytes() {
        var writer = new ByteWriter();
        writer.WriteFloat(1500f);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(FallDamageRules.Distance(reader.ReadFloat(), true), Is.EqualTo(500f));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

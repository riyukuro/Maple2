using System.Numerics;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class MinimapPingRulesTests {
    [Test]
    public void Bytes() {
        var writer = new ByteWriter();
        writer.WriteLong(1234567890123);
        writer.WriteByte(1);
        writer.WriteFloat(150f);
        writer.WriteFloat(-300f);
        writer.WriteFloat(1200f);
        var reader = new ByteReader(writer.ToArray(), 0);
        (long characterId, byte kind, Vector3 position) = MinimapPingRules.Read(reader);
        Assert.That(characterId, Is.EqualTo(1234567890123));
        Assert.That(kind, Is.EqualTo(1));
        Assert.That(position, Is.EqualTo(new Vector3(150f, -300f, 1200f)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Kind() {
        Assert.That(MinimapPingRules.KindAllowed(0), Is.True);
        Assert.That(MinimapPingRules.KindAllowed(1), Is.True);
        Assert.That(MinimapPingRules.KindAllowed(2), Is.False);
        Assert.That(MinimapPingRules.KindAllowed(255), Is.False);
    }

    [Test]
    public void Position() {
        Assert.That(MinimapPingRules.PositionSane(new Vector3(150f, -300f, 1200f)), Is.True);
        Assert.That(MinimapPingRules.PositionSane(Vector3.Zero), Is.True);
        Assert.That(MinimapPingRules.PositionSane(new Vector3(float.NaN, 0, 0)), Is.False);
        Assert.That(MinimapPingRules.PositionSane(new Vector3(0, float.PositiveInfinity, 0)), Is.False);
        Assert.That(MinimapPingRules.PositionSane(new Vector3(0, 0, -2_000_000f)), Is.False);
    }

    [Test]
    public void Rate() {
        Assert.That(MinimapPingRules.RateAllowed(0, 10), Is.True);
        Assert.That(MinimapPingRules.RateAllowed(1000, 1499), Is.False);
        Assert.That(MinimapPingRules.RateAllowed(1000, 1500), Is.True);
    }
}

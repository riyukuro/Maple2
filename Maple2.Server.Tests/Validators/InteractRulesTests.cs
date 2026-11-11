using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class InteractRulesTests {
    [Test]
    public void NamesBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(2);
        writer.WriteString("a1");
        writer.WriteString("b2");
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(InteractRules.ReadNames(reader), Is.EqualTo(new[] { "a1", "b2" }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Reach() {
        var player = new Vector3(0, 0, 0);
        Assert.That(InteractRules.InReach(player, new Vector3(300, 0, 0), 300), Is.True);
        Assert.That(InteractRules.InReach(player, new Vector3(300, 0, 0) + new Vector3(InteractRules.ReachSlack, 0, 0), 300), Is.True);
        Assert.That(InteractRules.InReach(player, new Vector3(451, 0, 0), 300), Is.False);
        Assert.That(InteractRules.InReach(player, new Vector3(0, 0, 500), 300), Is.False);
        Assert.That(InteractRules.InReach(player, new Vector3(9000, 0, 0), 0), Is.True);
    }

    [Test]
    public void CubeBegin() {
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.Farming), Is.True);
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.Breeding), Is.True);
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.Nurturing), Is.False);
        Assert.That(InteractRules.NeedsBegin(Maple2.Model.Enum.InteractCubeControlType.None), Is.False);
    }

    // Modes 4 and 5: i32 count, then a string id and a u8 per cube.
    [Test]
    public void CubeBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(1);
        writer.WriteUnicodeString("4_5_6");
        writer.WriteByte(1);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("4_5_6"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void EndNeedsStart() {
        var started = new HashSet<string> { "a1" };
        Assert.That(InteractRules.CanEnd(started, "a1"), Is.True);
        Assert.That(InteractRules.CanEnd(started, "b2"), Is.False);
        Assert.That(InteractRules.CanEnd(new HashSet<string>(), "a1"), Is.False);
    }
}

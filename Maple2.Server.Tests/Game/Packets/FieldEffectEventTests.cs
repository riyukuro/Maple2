using System;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game.Packets;

// The layout is the client's reader: GameEvent::vfunc_4 (the id), then FieldEffectContext::Read (a u8 count, the map
// ids, then the effect as a unicode string).
public class FieldEffectEventTests {
    [Test]
    public void GameEvent_FieldEffectEndsWithTheEffect() {
        var metadata = new GameEventMetadata(900090, GameEventType.FieldEffect, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1000), TimeSpan.Zero, TimeSpan.Zero, [],
            new FieldEffect([2000064, 2000065], "Snow"), "2000064,2000065", "Snow", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("FieldEffect"));
        Assert.That(reader.ReadInt(), Is.EqualTo(900090));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(2000064));
        Assert.That(reader.ReadInt(), Is.EqualTo(2000065));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Snow"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // An event with no effect still writes the string's length, so the next event in the list starts where the
    // client looks for it.
    [Test]
    public void GameEvent_FieldEffectWithNoEffectWritesAnEmptyString() {
        var metadata = new GameEventMetadata(900091, GameEventType.FieldEffect, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1000), TimeSpan.Zero, TimeSpan.Zero, [],
            new FieldEffect([2000064], ""), "2000064", "", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);

        byte[] name = "FieldEffect"u8.ToArray();
        int expected = 2 + name.Length * 2 + 4 + 1 + 4 + 2;
        Assert.That(writer.Length, Is.EqualTo(expected));
        Assert.That(BitConverter.ToUInt16(writer.Buffer, expected - 2), Is.EqualTo(0));
    }
}

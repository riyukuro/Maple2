using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class StateSyncRulesTests {
    // A base record: state, sub state, position (3 x i16), rotation i16, animation u8,
    // speed (3 x i16), the bounded byte, two i16, flags u8, sync number i32.
    private static byte[] Record(byte afterSpeed, byte subState = 0, byte direction = 0) {
        var writer = new ByteWriter();
        writer.WriteByte((byte) ActorState.Idle);
        writer.WriteByte(subState);
        writer.WriteShort(1);
        writer.WriteShort(2);
        writer.WriteShort(3);
        writer.WriteShort(900);
        writer.WriteByte(direction);
        if ((direction & 0x80) != 0) {
            writer.WriteFloat(0.5f);
            writer.WriteFloat(-0.5f);
        }
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteByte(afterSpeed);
        writer.WriteShort(0);
        writer.WriteShort(0);
        writer.WriteByte(0);
        writer.WriteInt(7);
        return writer.ToArray();
    }

    [TestCase((byte) 0, true)]
    [TestCase((byte) 2, true)]
    [TestCase((byte) 0x13, true)]
    [TestCase((byte) 0x14, false)]
    [TestCase((byte) 0xFF, false)]
    public void ByteAfterSpeedOfARecord(byte value, bool expected) {
        byte[] bytes = Record(value);
        var packet = new ByteReader(bytes, 0);
        var sync = new StateSync();
        sync.ReadFrom(packet);
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(sync.SyncNumber, Is.EqualTo(7));
        Assert.That(sync.Unknown1, Is.EqualTo(value));
        Assert.That(StateSyncRules.IsRelayable(sync.Unknown1), Is.EqualTo(expected));
    }

    [TestCase((byte) 0, true)]
    [TestCase((byte) 178, true)]
    [TestCase((byte) 179, false)]
    [TestCase((byte) 0xFF, false)]
    public void SubStateByteOfARecord(byte value, bool expected) {
        var packet = new ByteReader(Record(2, subState: value), 0);
        var sync = new StateSync();
        sync.ReadFrom(packet);
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That((byte) sync.SubState, Is.EqualTo(value));
        Assert.That(StateSyncRules.IsRelayable((byte) sync.SubState, sync.Unknown1), Is.EqualTo(expected));
    }

    // The two floats follow the direction byte whenever its top bit is set, not only for 0x80.
    [TestCase((byte) 0x00)]
    [TestCase((byte) 0x06)]
    [TestCase((byte) 0x80)]
    [TestCase((byte) 0x81)]
    public void DirectionByteOfARecord(byte direction) {
        byte[] bytes = Record(2, direction: direction);
        var packet = new ByteReader(bytes, 0);
        var sync = new StateSync();
        sync.ReadFrom(packet);
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(sync.SyncNumber, Is.EqualTo(7));
        Assert.That(sync.Unknown1, Is.EqualTo(2));

        var writer = new ByteWriter();
        sync.WriteTo(writer);
        Assert.That(writer.ToArray(), Is.EqualTo(bytes));
    }
}

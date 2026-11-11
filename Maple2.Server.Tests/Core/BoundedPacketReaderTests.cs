using System;
using System.Buffers;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Network;
using Maple2.Server.Game.PacketHandlers.Field;

namespace Maple2.Server.Tests.Core;

public class BoundedPacketReaderTests {
    // A packet in a buffer that is longer than the packet, as the rented copy of a field packet is.
    private static BoundedPacketReader InLongBuffer(byte[] packet, int offset = 0) {
        byte[] buffer = new byte[packet.Length + 64];
        Array.Fill(buffer, (byte) 0x7F);
        Array.Copy(packet, buffer, packet.Length);
        var copy = new FieldPacketReader(buffer, packet.Length, offset);
        return new BoundedPacketReader(copy, copy.PacketLength - copy.Position);
    }

    [Test]
    public void ReadsThePacketToItsLastByte() {
        var writer = new ByteWriter();
        writer.WriteByte(7);
        writer.WriteShort(-2);
        writer.WriteInt(123456);
        writer.WriteLong(long.MinValue);
        writer.WriteFloat(1.5f);
        writer.WriteBool(true);
        writer.WriteUnicodeString("ab");
        writer.WriteString("xyz");
        writer.WriteBytes(new byte[] { 1, 2, 3 });
        BoundedPacketReader reader = InLongBuffer(writer.ToArray());

        Assert.That(reader.Available, Is.EqualTo(writer.Length));
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadShort(), Is.EqualTo(-2));
        Assert.That(reader.Peek<int>(), Is.EqualTo(123456));
        Assert.That(reader.ReadInt(), Is.EqualTo(123456));
        Assert.That(reader.Read<long>(), Is.EqualTo(long.MinValue));
        Assert.That(reader.ReadFloat(), Is.EqualTo(1.5f));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadString(), Is.EqualTo("xyz"));
        Assert.That(reader.ReadBytes(2), Is.EqualTo(new byte[] { 1, 2 }));
        reader.Skip(1);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void StartsAfterTheBytesAlreadyRead() {
        // The router reads the opcode before the handler runs.
        BoundedPacketReader reader = InLongBuffer(new byte[] { 0x20, 0x00, 5, 6 }, offset: 2);

        Assert.That(reader.Available, Is.EqualTo(2));
        Assert.That(reader.ReadShort(), Is.EqualTo(0x0605));
        Assert.Throws<PacketOverreadException>(() => reader.ReadByte());
    }

    [Test]
    public void ReadPastThePacketReportsWantedAndLeft() {
        BoundedPacketReader reader = InLongBuffer(new byte[] { 1, 2, 3 });
        reader.ReadByte();

        var ex = Assert.Throws<PacketOverreadException>(() => reader.ReadInt());
        Assert.That(ex!.Wanted, Is.EqualTo(4));
        Assert.That(ex.Left, Is.EqualTo(2));
        // The failed read takes nothing.
        Assert.That(reader.Available, Is.EqualTo(2));
        Assert.That(reader.ReadShort(), Is.EqualTo(0x0302));
        Assert.Throws<PacketOverreadException>(() => reader.ReadBool());
        Assert.Throws<PacketOverreadException>(() => reader.Peek<byte>());
        Assert.Throws<PacketOverreadException>(() => reader.Skip(1));
        Assert.Throws<PacketOverreadException>(() => reader.ReadBytes(-1));
    }

    [Test]
    public void StringLongerThanThePacketIsAnOverread() {
        // Count 4, two characters present; the buffer behind the packet would supply the rest.
        var wide = Assert.Throws<PacketOverreadException>(() => InLongBuffer(new byte[] { 4, 0, 0x41, 0, 0x42, 0 }).ReadUnicodeString());
        Assert.That(wide!.Wanted, Is.EqualTo(10));
        Assert.That(wide.Left, Is.EqualTo(6));

        var narrow = Assert.Throws<PacketOverreadException>(() => InLongBuffer(new byte[] { 0xFF, 0xFF, 0x41 }).ReadString());
        Assert.That(narrow!.Wanted, Is.EqualTo(2 + 0xFFFF));
        Assert.That(narrow.Left, Is.EqualTo(3));

        // Not even the count is there.
        var count = Assert.Throws<PacketOverreadException>(() => InLongBuffer(new byte[] { 1 }).ReadString());
        Assert.That(count!.Wanted, Is.EqualTo(2));
        Assert.That(count.Left, Is.EqualTo(1));
    }

    [Test]
    public void PooledReaderEndsAtThePacket() {
        // The reader the session hands to the router: a rented buffer with the packet's own length.
        // PoolByteReader returns the buffer when it is disposed.
        byte[] rented = ArrayPool<byte>.Shared.Rent(100);
        rented[0] = 9;
        rented[1] = 8;
        using var pooled = new PoolByteReader(ArrayPool<byte>.Shared, rented, 2);
        Assert.That(pooled.Available, Is.EqualTo(2));
        var reader = new BoundedPacketReader(pooled, pooled.Available);
        Assert.That(reader.ReadShort(), Is.EqualTo(0x0809));
        Assert.Throws<PacketOverreadException>(() => reader.ReadByte());
    }
}

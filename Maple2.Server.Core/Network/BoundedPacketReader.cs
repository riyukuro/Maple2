using System.Runtime.CompilerServices;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Core.Network;

// A read that asks for more bytes than the packet has left.
public sealed class PacketOverreadException : Exception {
    public int Wanted { get; }
    public int Left { get; }

    public PacketOverreadException(int wanted, int left) : base($"Read of {wanted} bytes with {left} left in the packet") {
        Wanted = wanted;
        Left = left;
    }
}

// The reader a packet handler sees. It ends at the last byte of the packet, also when the buffer below it is
// longer (a rented copy), so a handler that reads more than the client wrote stops instead of reading other bytes.
public sealed class BoundedPacketReader : IByteReader {
    private readonly IByteReader inner;
    private int left;

    public BoundedPacketReader(IByteReader inner, int length) {
        this.inner = inner;
        left = Math.Max(0, length);
    }

    public int Available => left;

    private void Take(int count) {
        if (count < 0 || count > left) {
            throw new PacketOverreadException(count, left);
        }
        left -= count;
    }

    private void Check(int count) {
        if (count < 0 || count > left) {
            throw new PacketOverreadException(count, left);
        }
    }

    public T Read<T>() where T : struct {
        Take(Unsafe.SizeOf<T>());
        return inner.Read<T>();
    }

    public T Peek<T>() where T : struct {
        Check(Unsafe.SizeOf<T>());
        return inner.Peek<T>();
    }

    public byte[] ReadBytes(int count) {
        Take(count);
        return inner.ReadBytes(count);
    }

    public bool ReadBool() {
        Take(1);
        return inner.ReadBool();
    }

    public byte ReadByte() {
        Take(1);
        return inner.ReadByte();
    }

    public short ReadShort() {
        Take(2);
        return inner.ReadShort();
    }

    public int ReadInt() {
        Take(4);
        return inner.ReadInt();
    }

    public float ReadFloat() {
        Take(4);
        return inner.ReadFloat();
    }

    public long ReadLong() {
        Take(8);
        return inner.ReadLong();
    }

    // A string is a 16-bit count and then that many characters (one byte each, or two for the wide form).
    public string ReadString() {
        Take(StringSize(1));
        return inner.ReadString();
    }

    public string ReadUnicodeString() {
        Take(StringSize(2));
        return inner.ReadUnicodeString();
    }

    private int StringSize(int charSize) {
        Check(2);
        return 2 + inner.Peek<ushort>() * charSize;
    }

    public void Skip(int count) {
        Take(count);
        inner.Skip(count);
    }

    // The reader below belongs to whoever made it (the session or the field queue), which also releases it.
    public void Dispose() { }
}

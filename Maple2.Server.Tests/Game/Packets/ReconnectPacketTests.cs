using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Reconnect 0x05 (CReconnectionManagerCli, reconnectionmanagercli.cpp).
public class ReconnectPacketTests {
    // The refusal: opcode 0x0005, command 3 and nothing after it.
    [Test]
    public void RefuseBytes() {
        Assert.That(ReconnectPacket.Refuse().ToArray(), Is.EqualTo(new byte[] { 0x05, 0x00, 0x03 }));
    }

    // SendReconnectRequest: byte, i64 account, i64 character, i32 attempt, i32 sequence, unicode machine id, then
    // the context: i32 mode, string host, i32 port.
    [Test]
    public void RequestLayout() {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteLong(0x0102030405060708);
        writer.WriteLong(0x1112131415161718);
        writer.WriteInt(2);
        writer.WriteInt(345);
        writer.WriteUnicodeString("machine");
        writer.WriteInt(1);
        writer.WriteString("127.0.0.1");
        writer.WriteInt(20002);
        var reader = new ByteReader(writer.ToArray(), 0);

        ReconnectRequest request = ReconnectRequest.Read(reader);

        Assert.That(request, Is.EqualTo(new ReconnectRequest(1, 0x0102030405060708, 0x1112131415161718, 2, 345, "machine", 1, "127.0.0.1", 20002)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

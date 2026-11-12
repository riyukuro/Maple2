using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class AttendancePacketTests {
    // Client: CMyCharacter::FUN_140d9f1c0 reads an int count, then per entry an int event id and a long time
    // (FUN_140d9fc80, client/ms2/notifydebugstate.cpp:132).
    [Test]
    public void StartedEvents_CountThenIdAndTime() {
        ByteWriter writer = AttendancePacket.StartedEvents([(4321, 1700000000L), (4322, 1700086400L)]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Attendance));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That((reader.ReadInt(), reader.ReadLong()), Is.EqualTo((4321, 1700000000L)));
        Assert.That((reader.ReadInt(), reader.ReadLong()), Is.EqualTo((4322, 1700086400L)));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 2 * 12));
    }

    // What the server sends at login: no item starts an attendance event here.
    [Test]
    public void StartedEvents_EmptyIsModeAndZeroCount() {
        ByteWriter writer = AttendancePacket.StartedEvents([]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Attendance));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }
}

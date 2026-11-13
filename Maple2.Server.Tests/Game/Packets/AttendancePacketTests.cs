using Maple2.Model.Error;
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

    // Client: UIAttendGiftDialog::FUN_14102ecc0 (uiattendgiftdialog.cpp:363) reads a 4-byte event id.
    [Test]
    public void SelectEvent_ModeThenEventId() {
        ByteWriter writer = AttendancePacket.SelectEvent(4321);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Attendance));
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(4321));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }

    // Client: FUN_14102ed90 (uiattendgiftdialog.cpp:604) reads one byte; mode 5 has no body.
    [Test]
    public void ErrorAndRefreshAll_Bytes() {
        Assert.That(AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_not_found_evnet).ToArray()[2..], Is.EqualTo(new byte[] { 9, 6 }));
        Assert.That(AttendancePacket.RefreshAll().ToArray()[2..], Is.EqualTo(new byte[] { 5 }));
    }

    // An empty list: a player without an item-started event.
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

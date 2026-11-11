using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Validators;

public class GuideObjectSyncTests {
    // GuideObjectSync 0x0035: i16 kind, u8 count, then per record the UserSync record and one i32.
    // The record has two layouts: a packed direction byte, or 0x80 and two floats.
    private static byte[] Packet(short kind, params byte[] directions) {
        var writer = new ByteWriter();
        writer.WriteShort(kind);
        writer.WriteByte((byte) directions.Length);
        int number = 1;
        foreach (byte direction in directions) {
            writer.WriteByte((byte) ActorState.Idle);
            writer.WriteByte(0);
            writer.WriteShort(10);
            writer.WriteShort(20);
            writer.WriteShort((short) (30 + number));
            writer.WriteShort(900);
            writer.WriteByte(direction);
            if ((direction & 0x80) != 0) {
                writer.WriteFloat(0.5f);
                writer.WriteFloat(-0.5f);
            }
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteByte(2);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteByte(0);
            writer.WriteInt(number++);
            writer.WriteInt(123456); // the one tick after each record
        }
        return writer.ToArray();
    }

    [TestCase(new byte[] { 9 })] // packed direction
    [TestCase(new byte[] { 0x80 })] // two floats
    [TestCase(new byte[] { 9, 0x80 })] // the two per entry of furnishing mode
    [TestCase(new byte[] { 0x80, 5, 9 })]
    public void ReadsEachLayoutToTheEnd(byte[] directions) {
        var packet = new ByteReader(Packet(1, directions), 0);
        Assert.That(packet.ReadShort(), Is.EqualTo(1));

        StateSync[]? records = GuideObjectSyncHandler.ReadRecords(packet);

        Assert.That(records, Is.Not.Null);
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(records!.Length, Is.EqualTo(directions.Length));
        for (int i = 0; i < records.Length; i++) {
            Assert.That(records[i].SyncNumber, Is.EqualTo(i + 1));
            Assert.That(records[i].Animation, Is.EqualTo(directions[i]));
            Assert.That(records[i].Position.Z, Is.EqualTo(31 + i).Within(0.01f));
        }
    }

    [Test]
    public void NoRecordIsNotRelayed() {
        var packet = new ByteReader(Packet(1), 0);
        packet.ReadShort();
        Assert.That(GuideObjectSyncHandler.ReadRecords(packet), Is.Null);
    }
}

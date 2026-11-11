using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class JobErrorTests {
    // Client: CSceneStateGame::OnJob (client/ms2/scenestategame.cpp:7536) reads the int object id, the byte mode 3
    // and the byte code; code 18 alone reads one more int, the number put into its message (:7555).
    [Test]
    public void InventoryFullCarriesTheSlotCount() {
        ByteWriter writer = JobPacket.Error(77, JobError.s_err_job_inventory_full, 4);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Job));
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(18));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(writer.Length, Is.EqualTo(2 + 4 + 1 + 1 + 4));
    }

    [TestCase(JobError.s_err_job_bad_job, 2)]
    [TestCase(JobError.s_err_job_not_enough_meso, 8)]
    [TestCase(JobError.s_err_job_unknown, 255)]
    public void OtherCodesCarryNothing(JobError error, int code) {
        ByteWriter writer = JobPacket.Error(77, error, 4);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Job));
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(code));
        Assert.That(writer.Length, Is.EqualTo(2 + 4 + 1 + 1));
    }
}

using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class TriggerAgentTests {
    // Client: CTriggerService::FUN_141863a90 (client/ms2/triggerservice.cpp:951) reads the int id and hands the
    // rest to the object's class. An agent has the base class (client/ms2/triggeractioncli.cpp:450, :435), which
    // reads nothing, so the next object's id follows at once. A cube reads one bool (:818).
    [Test]
    public void LoadWritesAgentAsIdOnly() {
        ITriggerObject[] objects = [
            new TriggerObjectCube(new Ms2TriggerCube(101, true)) { Visible = true },
            new TriggerObjectAgent(new Ms2TriggerAgent(201, true)) { Visible = true },
            new TriggerObjectAgent(new Ms2TriggerAgent(202, false)),
        ];
        ByteWriter writer = TriggerPacket.Load(objects);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Trigger));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(101));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(201));
        Assert.That(reader.ReadInt(), Is.EqualTo(202));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 5 + 4 + 4));
    }

    [Test]
    public void UpdateWritesAgentAsIdOnly() {
        ByteWriter writer = TriggerPacket.Update(new TriggerObjectAgent(new Ms2TriggerAgent(201, true)) { Visible = true });
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Trigger));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(201));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }
}

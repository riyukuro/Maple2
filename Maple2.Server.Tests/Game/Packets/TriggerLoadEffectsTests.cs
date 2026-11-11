using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class TriggerLoadEffectsTests {
    // Client: WorldService::FUN_141bd42f0 (client/ms2/worldservice_1.cpp:7302), map reader worldservice_2.cpp:225,
    // GameObjectEffectNifCtx::Read (gameobjecteffectnifctx.cpp:68): int count, per entry int key, then int object
    // id, text nif path, bool, float, float.
    [Test]
    public void LoadEffectsWritesKeyThenEffectBlock() {
        ByteWriter writer = TriggerPacket.LoadEffects([(301, "a.nif", true, 1.5f, 90f), (302, "bc.nif", false, 1f, 0f)]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Trigger));
        Assert.That(reader.ReadByte(), Is.EqualTo(16));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(301)); // key
        Assert.That(reader.ReadInt(), Is.EqualTo(301)); // object id
        Assert.That(reader.ReadString(), Is.EqualTo("a.nif"));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadFloat(), Is.EqualTo(1.5f));
        Assert.That(reader.ReadFloat(), Is.EqualTo(90f));
        Assert.That(reader.ReadInt(), Is.EqualTo(302));
        Assert.That(reader.ReadInt(), Is.EqualTo(302));
        Assert.That(reader.ReadString(), Is.EqualTo("bc.nif"));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadFloat(), Is.EqualTo(1f));
        Assert.That(reader.ReadFloat(), Is.EqualTo(0f));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + (4 + 4 + 2 + 5 + 1 + 8) + (4 + 4 + 2 + 6 + 1 + 8)));
    }

    [Test]
    public void LoadEffectsEmptyIsCountOnly() {
        ByteWriter writer = TriggerPacket.LoadEffects([]);
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }
}

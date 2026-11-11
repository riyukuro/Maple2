using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// createInteractObject: the client's SSG_INTERACT_ADD fields, and the lifeTime after which the server removes it.
public class AiInteractTests {
    private static readonly AiMetadata.CreateInteractObjectNode Chest = new("createInteractObject", [], "Opened_A",
        12000072, 120, "Interaction_chest_C01", "Closed_A");

    [Test]
    public void AddPacket() {
        var position = new Vector3(100, -200, 300);
        var rotation = new Vector3(0, 0, 90);
        InteractMeshObject interact = AiInteract.Create(42, Chest, position, rotation);
        ByteWriter writer = InteractObjectPacket.Add(interact);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.InteractObject));
            reader.ReadByte(); // command
            Assert.That(reader.ReadString(), Is.EqualTo("AiCreate_42"));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractState.Reactable));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractType.Mesh));
            Assert.That(reader.ReadInt(), Is.EqualTo(12000072));
            Assert.That(new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo(position));
            Assert.That(new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo(rotation));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("MS2InteractActor"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Interaction_chest_C01"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Opened_A"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Closed_A"));
            Assert.That(reader.ReadFloat(), Is.EqualTo(1f));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // The two nodes give 20 and 120.
    [TestCase(20, 20)]
    [TestCase(120, 120)]
    [TestCase(0, 0)]
    public void LifeTimeIsSeconds(int lifeTime, int seconds) {
        Assert.That(AiInteract.LifeTime(lifeTime), Is.EqualTo(TimeSpan.FromSeconds(seconds)));
    }

    // The client reads the name and an effect string.
    [Test]
    public void RemovePacket() {
        ByteWriter writer = InteractObjectPacket.Remove(AiInteract.EntityId(42));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.InteractObject));
            reader.ReadByte();
            Assert.That(reader.ReadString(), Is.EqualTo("AiCreate_42"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo(""));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }
}

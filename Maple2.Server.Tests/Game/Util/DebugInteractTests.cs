using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// InteractObject 9, the GM object create dialog: the sent row and strings become the client's SSG_INTERACT_ADD.
public class DebugInteractTests {
    [Test]
    public void AddPacket() {
        var position = new Vector3(150, -300, 1200);
        var rotation = new Vector3(0, 0, 180);
        InteractMeshObject interact = DebugInteract.Create(7, 10000001, "MS2InteractMesh", "interaction_chestA_02",
            "Opened_A", "Idle_A", position, rotation);
        ByteWriter writer = InteractObjectPacket.Add(interact);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.InteractObject));
            Assert.That(reader.ReadByte(), Is.EqualTo(9)); // SSG_INTERACT_ADD
            Assert.That(reader.ReadString(), Is.EqualTo("DebugCreate_7"));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractState.Reactable));
            // The client adds kind 1 (mesh) and 8 (billboard) only.
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractType.Mesh));
            Assert.That(reader.ReadInt(), Is.EqualTo(10000001));
            Assert.That(new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo(position));
            Assert.That(new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), Is.EqualTo(rotation));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("MS2InteractMesh"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("interaction_chestA_02"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Opened_A"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Idle_A"));
            Assert.That(reader.ReadFloat(), Is.EqualTo(1f));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    [Test]
    public void EachPressIsItsOwnObject() {
        Assert.That(DebugInteract.EntityId(7), Is.Not.EqualTo(DebugInteract.EntityId(8)));
    }
}

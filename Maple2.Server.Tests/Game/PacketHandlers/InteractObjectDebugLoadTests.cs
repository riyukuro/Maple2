using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// InteractObject 0x0031 mode 9 as the client's object create dialog writes it (GM only): i32 interact id, four
// unicode strings (model, asset, normal state, reactable), i32 count, the GM's position and rotation. And the field's
// object list, InteractObject 0x0063 mode 8: i32 count, then (wstr id, u8 state, u8 type) per object and an i32
// respawn tick after a gathered one.
public class InteractObjectDebugLoadTests {
    private const int InteractId = 11;

    private static InteractObjectMetadata Row(int id, InteractType type) {
        return new InteractObjectMetadata(id, type, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
    }

    private static TestGameSession Start(bool gm) {
        var test = new TestGameSession();
        test.Start();
        test.SetTable(new InteractObjectTable(new Dictionary<int, InteractObjectMetadata> {
            [InteractId] = Row(InteractId, InteractType.Mesh),
        }));
        // The field's chests, where a debug object is kept (the test field is built without them).
        typeof(FieldManager).GetField("fieldChests", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(test.Session.Field, new ConcurrentDictionary<string, FieldInteract>());
        if (gm) {
            test.Session.Player.AdminPermissions = AdminPermissions.GameMaster;
        }
        test.TakeSent();
        return test;
    }

    private static void Create(TestGameSession test, int interactId) {
        new InteractObjectHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteInt(interactId);
            writer.WriteUnicodeString("model");
            writer.WriteUnicodeString("asset");
            writer.WriteUnicodeString("normal");
            writer.WriteUnicodeString("reactable");
            writer.WriteInt(1);
            writer.Write(new Vector3(100, 200, 300));
            writer.Write(new Vector3(0, 0, 90));
        }));
    }

    private static List<byte[]> Adds(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.InteractObject && packet[2] == 9).ToList();
    }

    // A GM's object is put in the field at the GM's place, with the dialog's names, and shown to the field.
    [Test]
    public void DebugCreate_FromAGm_AddsTheObject() {
        TestGameSession test = Start(gm: true);

        Create(test, InteractId);

        List<byte[]> adds = Adds(test);
        Assert.That(adds, Has.Count.EqualTo(1));
        string entityId = new ByteReader(adds[0], 3).ReadString();
        Assert.That(test.Session.Field!.TryGetInteract(entityId, out FieldInteract? added), Is.True);
        var mesh = (InteractMeshObject) added!.Object;
        Assert.Multiple(() => {
            Assert.That(added.Value.Id, Is.EqualTo(InteractId));
            Assert.That(mesh.Model, Is.EqualTo("model"));
            Assert.That(mesh.Asset, Is.EqualTo("asset"));
            Assert.That(mesh.NormalState, Is.EqualTo("normal"));
            Assert.That(mesh.Reactable, Is.EqualTo("reactable"));
            Assert.That(added.Position, Is.EqualTo(new Vector3(100, 200, 300)));
        });
    }

    // An id the interact object table does not have adds nothing.
    [Test]
    public void DebugCreate_UnknownRow_AddsNothing() {
        TestGameSession test = Start(gm: true);

        Create(test, InteractId + 1);

        Assert.That(Adds(test), Is.Empty);
    }

    // From a player the packet is dropped.
    [Test]
    public void DebugCreate_FromAPlayer_IsDropped() {
        TestGameSession test = Start(gm: false);

        Create(test, InteractId);

        Assert.That(Adds(test), Is.Empty);
    }

    // The object list: a gathered object carries the respawn tick (0), a mesh object does not.
    [Test]
    public void Load_WritesEachObject() {
        var test = new TestGameSession();
        test.Start();
        FieldInteract mesh = test.AddInteract(Row(21, InteractType.Mesh), new InteractMeshObject("m1", new Ms2InteractMesh(21, Vector3.Zero, Vector3.Zero)));
        FieldInteract gather = test.AddInteract(Row(22, InteractType.Gathering), new InteractMeshObject("g1", new Ms2InteractMesh(22, Vector3.Zero, Vector3.Zero)));

        byte[] packet = InteractObjectPacket.Load(new[] { mesh, gather }).ToArray();

        var reader = new ByteReader(packet, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(8));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadString(), Is.EqualTo("m1"));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractState.Reactable));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractType.Mesh));
            Assert.That(reader.ReadString(), Is.EqualTo("g1"));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractState.Reactable));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) InteractType.Gathering));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }
}

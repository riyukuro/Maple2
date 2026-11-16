using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The InteractObject modes the add, remove and hold tests do not cover, read as CSceneStateGame::OnInteractObject
// (scenestategame.cpp:6218) reads them: 4 (state) an ansi entity name, a byte state, a byte kind; 5 (message) the name,
// a byte kind, then the object's own reader; 6 (kind state) an int and a byte state; 7 (all state) a byte; 8 (states) an
// int count, per object the name, a byte state, a byte kind and, for a gathering object, an int; 13 (result) a byte
// result, the name and a byte kind; 14 (notice) a short, two ints and a unicode string.
public class InteractObjectModeLayoutTests {
    private TestGameSession test = null!;

    private FieldInteract Interact(InteractType type) {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        var row = new InteractObjectMetadata(61, type, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
        IInteractObject obj = type == InteractType.Gathering
            ? new InteractGatheringObject("ent", new Ms2InteractActor(61, Vector3.Zero, Vector3.Zero))
            : new InteractMeshObject("ent", new Ms2InteractMesh(61, Vector3.Zero, Vector3.Zero));
        return test.AddInteract(row, obj);
    }

    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.InteractObject));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void StateMessageAndKindState() {
        FieldInteract mesh = Interact(InteractType.Mesh);

        ByteReader update = Open(InteractObjectPacket.Update(mesh), 4);
        Assert.That(update.ReadString(), Is.EqualTo("ent"));
        Assert.That(update.ReadByte(), Is.EqualTo((byte) InteractState.Reactable));
        Assert.That(update.ReadByte(), Is.EqualTo((byte) InteractType.Mesh));
        Assert.That(update.Available, Is.Zero);

        ByteReader message = Open(InteractObjectPacket.Interact(mesh), 5);
        Assert.That(message.ReadString(), Is.EqualTo("ent"));
        Assert.That(message.ReadByte(), Is.EqualTo((byte) InteractType.Mesh));
        Assert.That(message.Available, Is.Zero);

        ByteReader setState = Open(InteractObjectPacket.SetState(mesh), 6);
        Assert.That(setState.ReadInt(), Is.EqualTo(61));
        Assert.That(setState.ReadByte(), Is.EqualTo((byte) InteractState.Reactable));
        Assert.That(setState.Available, Is.Zero);

        ByteReader all = Open(InteractObjectPacket.Unknown7(1), 7);
        Assert.That(all.ReadByte(), Is.EqualTo(1));
        Assert.That(all.Available, Is.Zero);
    }

    [Test]
    public void LoadResultAndNotice() {
        FieldInteract gather = Interact(InteractType.Gathering);
        ByteReader load = Open(InteractObjectPacket.Load([gather]), 8);
        Assert.That(load.ReadInt(), Is.EqualTo(1));
        Assert.That(load.ReadString(), Is.EqualTo("ent"));
        Assert.That(load.ReadByte(), Is.EqualTo((byte) InteractState.Reactable));
        Assert.That(load.ReadByte(), Is.EqualTo((byte) InteractType.Gathering));
        Assert.That(load.ReadInt(), Is.Zero); // the gathering object's respawn tick
        Assert.That(load.Available, Is.Zero);

        FieldInteract mesh = Interact(InteractType.Mesh);
        ByteReader meshLoad = Open(InteractObjectPacket.Load([mesh]), 8);
        Assert.That(meshLoad.ReadInt(), Is.EqualTo(1));
        meshLoad.ReadString();
        meshLoad.ReadByte();
        meshLoad.ReadByte();
        Assert.That(meshLoad.Available, Is.Zero);

        ByteReader result = Open(InteractObjectPacket.Result(InteractResult.s_interact_find_new_telescope, mesh), 13);
        Assert.That(result.ReadByte(), Is.EqualTo((byte) InteractResult.s_interact_find_new_telescope));
        Assert.That(result.ReadString(), Is.EqualTo("ent"));
        Assert.That(result.ReadByte(), Is.EqualTo((byte) InteractType.Mesh));
        Assert.That(result.Available, Is.Zero);

        ByteReader notice = Open(InteractObjectPacket.Unknown14(), 14);
        Assert.That(notice.ReadShort(), Is.EqualTo(1));
        notice.ReadInt();
        notice.ReadInt();
        Assert.That(notice.ReadUnicodeString(), Is.Empty);
        Assert.That(notice.Available, Is.Zero);
    }
}

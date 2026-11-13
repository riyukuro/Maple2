using System;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// InteractObject against CSceneStateGame::OnInteractObject (scenestategame.cpp:6218) and Breakable against
// OnBreakable (:3667).
public class InteractObjectLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static void Head(ByteWriter expected, InteractType type) {
        expected.WriteByte(9);
        expected.WriteString("entity");
        expected.WriteByte((byte) InteractState.Reactable);
        expected.WriteByte((byte) type);
        expected.WriteInt(0x01020304);
        expected.WriteFloat(1);
        expected.WriteFloat(2);
        expected.WriteFloat(3);
        expected.WriteFloat(0);
        expected.WriteFloat(0);
        expected.WriteFloat(90);
        expected.WriteUnicodeString("MS2InteractMesh");
        expected.WriteUnicodeString("asset");
        expected.WriteUnicodeString("normal");
        expected.WriteUnicodeString("reactable");
        expected.WriteFloat(2);
        expected.WriteBool(false);
    }

    private static readonly Ms2InteractMesh Metadata = new(0x01020304, new Vector3(1, 2, 3), new Vector3(0, 0, 90));

    // Mode 9: narrow entity id, byte state, byte kind, int row, two Vector3, unicode model, asset, normal and reactable
    // states, float scale, bool. A kind 8 (billboard) object adds long board uid + unicode owner name; the client reads
    // them when the model is MS2InteractMesh or MS2InteractActor, which every billboard item's model is.
    [Test]
    public void Add() {
        var mesh = new InteractMeshObject("entity", Metadata) {
            Model = "MS2InteractMesh", Asset = "asset", NormalState = "normal", Reactable = "reactable", Scale = 2,
        };
        var expected = new ByteWriter();
        Head(expected, InteractType.Mesh);
        Assert.That(Body(InteractObjectPacket.Add(mesh)), Is.EqualTo(expected.ToArray()));

        var board = new InteractBillBoardObject("entity", Metadata, new Character { Name = "owner", Mastery = new Mastery() }) {
            Model = "MS2InteractMesh", Asset = "asset", NormalState = "normal", Reactable = "reactable", Scale = 2,
            BoardUid = 0x0102030405060708,
        };
        var billboard = new ByteWriter();
        Head(billboard, InteractType.BillBoard);
        billboard.WriteLong(0x0102030405060708);
        billboard.WriteUnicodeString("owner");
        Assert.That(Body(InteractObjectPacket.Add(board)), Is.EqualTo(billboard.ToArray()));
    }

    // Mode 10: narrow entity id, unicode effect. Mode 15: int, int.
    [Test]
    public void RemoveAndHold() {
        var remove = new ByteWriter();
        remove.WriteByte(10);
        remove.WriteString("entity");
        remove.WriteUnicodeString("effect");
        Assert.That(Body(InteractObjectPacket.Remove("entity", "effect")), Is.EqualTo(remove.ToArray()));

        var hold = new ByteWriter();
        hold.WriteByte(15);
        hold.WriteInt(0x11);
        hold.WriteInt(0x22);
        Assert.That(Body(InteractObjectPacket.Hold(0x11, 0x22)), Is.EqualTo(hold.ToArray()));
    }

    // Breakable: byte mode, mode 0 an int count (mode 1 one entry); each entry is a narrow entity id, byte state, byte
    // visible, int, int.
    [Test]
    public void Breakable() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        var actor = new BreakableActor(false, 7, 0, 0, 0, new Vector3(1, 2, 3), new Vector3(0, 0, 0));
        var breakable = new FieldBreakable(test.Session.Field!, 0x31, "entity", actor);

        var single = new ByteWriter();
        single.WriteByte(1);
        single.WriteString("entity");
        single.WriteByte((byte) BreakableState.Hide);
        single.WriteBool(false);
        single.WriteInt(0);
        single.WriteInt(0);
        Assert.That(Body(BreakablePacket.Update(breakable)), Is.EqualTo(single.ToArray()));

        var batch = new ByteWriter();
        batch.WriteByte(0);
        batch.WriteInt(1);
        batch.WriteString("entity");
        batch.WriteByte((byte) BreakableState.Hide);
        batch.WriteBool(false);
        batch.WriteInt(0);
        batch.WriteInt(0);
        Assert.That(Body(BreakablePacket.Update([breakable])), Is.EqualTo(batch.ToArray()));
    }
}

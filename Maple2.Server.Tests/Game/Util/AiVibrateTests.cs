using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Tools.VectorMath;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// showVibrate shows a hidden vibrate group, hideVibrateAll hides the shown objects again.
public class AiVibrateTests {
    private static FieldVibrateEntity Rock(string id, int group, int index = 0) =>
        new(new FieldEntityId(1, (ulong) index + 1, id), new Vector3(10, 20, 30), new Vector3(0, 0, 90), 1f,
            new BoundingBox3(new Vector3(-75, -75, 0), new Vector3(75, 75, 150)), 5, 3000, index, group);

    private static AiVibrate Field() => new([Rock("a1", 1), Rock("a2", 1), Rock("b1", 2), Rock("plain", 0), Rock("", 3)]);

    [Test]
    public void GroupObjectsStartHidden() {
        Assert.That(Field().Hidden, Is.EquivalentTo(new[] { "a1", "a2", "b1" }));
    }

    [Test]
    public void ShowShowsOnlyTheGroupOnce() {
        AiVibrate vibrate = Field();
        Assert.Multiple(() => {
            Assert.That(vibrate.Show(1), Is.EqualTo(new[] { "a1", "a2" }));
            Assert.That(vibrate.Show(1), Is.Empty);
            Assert.That(vibrate.Show(0), Is.Empty);
            Assert.That(vibrate.Show(9), Is.Empty);
            Assert.That(vibrate.Hidden, Is.EqualTo(new[] { "b1" }));
        });
    }

    [Test]
    public void HideAllHidesTheShownOnes() {
        AiVibrate vibrate = Field();
        vibrate.Show(2);
        vibrate.Show(1);
        Assert.Multiple(() => {
            Assert.That(vibrate.HideAll(), Is.EquivalentTo(new[] { "a1", "a2", "b1" }));
            Assert.That(vibrate.HideAll(), Is.Empty);
            Assert.That(vibrate.Hidden, Is.EquivalentTo(new[] { "a1", "a2", "b1" }));
            Assert.That(vibrate.Show(1), Is.EqualTo(new[] { "a1", "a2" }));
        });
    }

    // The map data keeps the hideGroupId after the break fields.
    [Test]
    public void MapDataKeepsTheGroup() {
        var structure = new FieldAccelerationStructure();
        var writer = new ByteWriter();
        structure.WriteTo(Rock("rock", 7, 4), writer);
        var reader = new ByteReader(writer.Buffer, 0);
        var entity = (FieldVibrateEntity) structure.ReadEntity(reader);
        Assert.Multiple(() => {
            Assert.That(entity.HideGroupId, Is.EqualTo(7));
            Assert.That(entity.VibrateIndex, Is.EqualTo(4));
            Assert.That(entity.BreakDefense, Is.EqualTo(5));
            Assert.That(entity.BreakTick, Is.EqualTo(3000));
            Assert.That(entity.Id.Id, Is.EqualTo("rock"));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // CSceneStateGame::OnShowVibrate / OnHideVibrate read one string: the object's entity name.
    [TestCase(true)]
    [TestCase(false)]
    public void PacketsSendTheName(bool show) {
        ByteWriter writer = show ? VibratePacket.Show("rock") : VibratePacket.Hide("rock");
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(show ? SendOp.ShowVibrate : SendOp.HideVibrate));
            Assert.That(reader.ReadString(), Is.EqualTo("rock"));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }
}

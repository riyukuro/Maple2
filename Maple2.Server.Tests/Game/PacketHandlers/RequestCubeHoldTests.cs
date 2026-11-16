using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 0x0037 mode 1, the cube the builder picks from the palette: the cube record (i32 item id, i64 uid,
// i64 expiry, bool template). The builder holds it and the field sees it (ResponseCube mode 1: i32 object id, the
// same record).
public class RequestCubeHoldTests {
    private const int ItemId = 50100001;
    private const long Uid = 77;

    private static List<byte[]> Hold(TestGameSession test) {
        test.TakeSent();
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(ItemId);
            writer.WriteLong(Uid);
            writer.WriteLong(0);
            writer.WriteBool(false);
        }));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ResponseCube).ToList();
    }

    // In build mode the cube is held and shown to the field.
    [Test]
    public void InBuildMode_TheCubeIsHeld() {
        var test = new TestGameSession();
        test.Start();
        test.Session.GuideObject = new FieldGuideObject(null!, 17, new ConstructionGuideObject());

        List<byte[]> sent = Hold(test);

        Assert.That(test.Session.HeldCube, Is.Not.Null);
        Assert.That(test.Session.HeldCube!.ItemId, Is.EqualTo(ItemId));
        Assert.That(test.Session.HeldCube.Id, Is.EqualTo(Uid));
        Assert.That(sent, Has.Count.EqualTo(1));
        var reader = new ByteReader(sent[0], 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(test.Session.Player.ObjectId));
            Assert.That(reader.ReadInt(), Is.EqualTo(ItemId));
            Assert.That(reader.ReadLong(), Is.EqualTo(Uid));
        });
    }

    // Out of build mode (no guide, or a guide of another kind) nothing is held.
    [Test]
    public void OutOfBuildMode_NothingIsHeld() {
        var test = new TestGameSession();
        test.Start();

        Assert.That(Hold(test), Is.Empty);
        test.Session.GuideObject = new FieldGuideObject(null!, 17, new SkillMagicControlGuide());
        Assert.That(Hold(test), Is.Empty);
        Assert.That(test.Session.HeldCube, Is.Null);
    }
}

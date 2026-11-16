using System.Numerics;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// HomeAction 7 (the ball), read as CSceneStateGame::OnHomeAction's case 7 hands it to FUN_140dee4a0 (ugcballobject.cpp:317):
// a byte kind, then 0 (create): an int object id, an int64 owner, a point and a float radius; 1 (remove): an int object id;
// 2 (update, CUGCBallObject::FUN_140dee050): an int object id, an int64 sender, then position, velocity and angular
// velocity points; 3 (hit, FUN_140dee220): an int object id, an int64 id, then a force and a hit point.
public class HomeBallLayoutTests {
    private static FieldGuideObject Ball() {
        return new FieldGuideObject(null!, 0x0A0B0C, new BallGuideObject(2.5f)) { CharacterId = 0x0102030405, Position = new Vector3(1, 2, 3) };
    }

    private static ByteReader Open(ByteWriter writer, byte kind) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.HomeAction));
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadByte(), Is.EqualTo(kind));
        return reader;
    }

    private static Vector3 Point(ByteReader reader) => new(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

    [Test]
    public void AddAndRemove() {
        ByteReader add = Open(HomeActionPacket.AddBall(Ball()), 0);
        Assert.That(add.ReadInt(), Is.EqualTo(0x0A0B0C));
        Assert.That(add.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(Point(add), Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(add.ReadFloat(), Is.EqualTo(2.5f));
        Assert.That(add.Available, Is.Zero);

        ByteReader remove = Open(HomeActionPacket.RemoveBall(Ball()), 1);
        Assert.That(remove.ReadInt(), Is.EqualTo(0x0A0B0C));
        Assert.That(remove.Available, Is.Zero);
    }

    [Test]
    public void UpdateAndHit() {
        ByteReader update = Open(HomeActionPacket.UpdateBall(Ball(), new Vector3(4, 5, 6), new Vector3(7, 8, 9)), 2);
        Assert.That(update.ReadInt(), Is.EqualTo(0x0A0B0C));
        Assert.That(update.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(Point(update), Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(Point(update), Is.EqualTo(new Vector3(4, 5, 6)));
        Assert.That(Point(update), Is.EqualTo(new Vector3(7, 8, 9)));
        Assert.That(update.Available, Is.Zero);

        ByteReader hit = Open(HomeActionPacket.HitBall(Ball(), new Vector3(4, 5, 6), new Vector3(7, 8, 9)), 3);
        Assert.That(hit.ReadInt(), Is.EqualTo(0x0A0B0C));
        Assert.That(hit.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(Point(hit), Is.EqualTo(new Vector3(4, 5, 6)));
        Assert.That(Point(hit), Is.EqualTo(new Vector3(7, 8, 9)));
        Assert.That(hit.Available, Is.Zero);
    }
}

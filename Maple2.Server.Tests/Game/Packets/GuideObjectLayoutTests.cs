using System.Numerics;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// GuideObject against CSceneStateGame::OnGuideObject (scenestategame.cpp): u8 mode; 0: u16 type, int guide id, long
// owner uid, vector3 position, vector3 direction, then by type: 0 (construction) a long item id, 1 (fishing) none, 2
// (skill magic control) long tag, int skill id, short level, u8 source level, u8 source index, u8 count x (long, u8);
// 1: int guide id, long owner uid; 2: int guide id, the guide's own state body; 3: int guide id, long owner uid.
public class GuideObjectLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.GuideObject));
        return reader;
    }

    private static FieldGuideObject Guide(IGuideObject value) =>
        new(null!, 17, value) { CharacterId = 900, Position = new Vector3(1, 2, 3), Heading = new Vector3(0, 0, 90) };

    private static void ReadHeader(ByteReader reader, short type) {
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(type));
        Assert.That(reader.ReadInt(), Is.EqualTo(17));
        Assert.That(reader.ReadLong(), Is.EqualTo(900));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(0, 0, 90)));
    }

    [Test]
    public void Create_ByType() {
        ByteReader reader = Read(GuideObjectPacket.Create(Guide(new ConstructionGuideObject())));
        ReadHeader(reader, 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuideObjectPacket.Create(Guide(new SkillMagicControlGuide())));
        ReadHeader(reader, 2);
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 0, 0, 0 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Remove() {
        ByteReader reader = Read(GuideObjectPacket.Remove(Guide(new ConstructionGuideObject())));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(17));
        Assert.That(reader.ReadLong(), Is.EqualTo(900));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

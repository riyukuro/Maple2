using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: CSceneStateGame::OnRegionSkillDamage (client/ms2/scenestategame.cpp) reads long skill uid, int caster,
// int region, byte attack point, byte count; per target int id, byte entries, 3 shorts, 3 floats, then per entry
// byte type, long amount. Made-up ids and positions.
public class RegionDamagePacketTests {
    private static readonly (DamageType, long)[] NoDamage = [];

    [Test]
    public void ATargetOfAnAttackWithoutDamageHasNoEntry() {
        // A region that only gives a buff, on its own caster: 19 bytes of head, 23 for the target, nothing after.
        ByteWriter writer = SkillDamagePacket.Region(0, 1001, 2002, 0, [
            new SkillDamagePacket.RegionTarget(1001, new Vector3(22.29f, -922.84f, 3136.46f), new Vector3(0, 0, 1), NoDamage),
        ]);
        byte[] expected = [
            0x3D, 0x00, 0x05,
            0, 0, 0, 0, 0, 0, 0, 0,
            0xE9, 0x03, 0, 0,
            0xD2, 0x07, 0, 0,
            0x00, 0x01,
            0xE9, 0x03, 0, 0, 0x00,
            0x16, 0x00, 0x66, 0xFC, 0x40, 0x0C, // 22, -922, 3136: cut, not rounded
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0x3F,
        ];
        Assert.That(writer.Buffer.Take(writer.Length).ToArray(), Is.EqualTo(expected));
    }

    // Client: CSceneStateGame::OnSplashDamage reads long skill uid, int skill id, short level, byte count; per
    // target the same as above.
    [Test]
    public void ATilePacketNamesOneTargetAndTheBlock() {
        ByteWriter writer = SkillDamagePacket.Tile(70000017, 1,
            new SkillDamagePacket.RegionTarget(1001, new Vector3(0, -1200, 3000), new Vector3(0, 0, 1), NoDamage));
        byte[] expected = [
            0x3D, 0x00, 0x06,
            0, 0, 0, 0, 0, 0, 0, 0, // no skill uid
            0x91, 0x1D, 0x2C, 0x04,
            0x01, 0x00,
            0x01,
            0xE9, 0x03, 0, 0, 0x00,
            0x00, 0x00, 0x50, 0xFB, 0xB8, 0x0B,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0x3F,
        ];
        Assert.That(writer.Buffer.Take(writer.Length).ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void ATilePacketWithDamageHasItsEntry() {
        ByteWriter writer = SkillDamagePacket.Tile(70000008, 1,
            new SkillDamagePacket.RegionTarget(1001, new Vector3(150, 300, 450), new Vector3(0, 0.6f, 0.8f), [(DamageType.Normal, 55)]));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillDamage));
            Assert.That(reader.ReadByte(), Is.EqualTo(6));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(70000008));
            Assert.That(reader.ReadShort(), Is.EqualTo(1));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(1001));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadShort(), Is.EqualTo(150));
            Assert.That(reader.ReadShort(), Is.EqualTo(300));
            Assert.That(reader.ReadShort(), Is.EqualTo(450));
            reader.Skip(12);
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(55));
            Assert.That(writer.Length, Is.EqualTo(2 + 16 + 23 + 9));
        });
    }

    [Test]
    public void TargetsWithAndWithoutDamageFollowEachOther() {
        ByteWriter writer = SkillDamagePacket.Region(7, 1001, 2002, 1, [
            new SkillDamagePacket.RegionTarget(3003, new Vector3(100.9f, 200.5f, -0.9f), Vector3.Zero, [(DamageType.Critical, 1234)]),
            new SkillDamagePacket.RegionTarget(1001, new Vector3(100.9f, 200.5f, -0.9f), Vector3.Zero, NoDamage),
        ]);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillDamage));
            Assert.That(reader.ReadByte(), Is.EqualTo(5));
            Assert.That(reader.ReadLong(), Is.EqualTo(7));
            Assert.That(reader.ReadInt(), Is.EqualTo(1001));
            Assert.That(reader.ReadInt(), Is.EqualTo(2002));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadByte(), Is.EqualTo(2));

            Assert.That(reader.ReadInt(), Is.EqualTo(3003));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadShort(), Is.EqualTo(100));
            Assert.That(reader.ReadShort(), Is.EqualTo(200));
            Assert.That(reader.ReadShort(), Is.EqualTo(0));
            reader.Skip(12);
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(1234));

            Assert.That(reader.ReadInt(), Is.EqualTo(1001));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            reader.Skip(18);
            Assert.That(writer.Length, Is.EqualTo(2 + 19 + 23 + 9 + 23));
        });
    }
}

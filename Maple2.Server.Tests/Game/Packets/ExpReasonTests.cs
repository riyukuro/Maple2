using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ExpReasonTests {
    // The reason KMS2 names on each kind of exp gain.
    [TestCase(ExpType.monster, 60001)]
    [TestCase(ExpType.quest, 1010)]
    [TestCase(ExpType.mission, 1053)]
    [TestCase(ExpType.expDrop, 1062)]
    [TestCase(ExpType.dungeonRelative, 1085)]
    [TestCase(ExpType.miniGame, 3007)]
    [TestCase(ExpType.gathering, 3103)]
    [TestCase(ExpType.manufacturing, 3107)]
    [TestCase(ExpType.petTaming, 3200)]
    [TestCase(ExpType.guildUserExp, 3301)]
    [TestCase(ExpType.mapCommon, 3001)]
    [TestCase(ExpType.mapHidden, 3002)]
    [TestCase(ExpType.taxi, 3000)]
    [TestCase(ExpType.telescope, 3003)]
    [TestCase(ExpType.fishing, 1059)]
    [TestCase(ExpType.musicMastery1, 1063)]
    [TestCase(ExpType.arcade, 1071)]
    [TestCase(ExpType.normalChest, 1091)]
    [TestCase(ExpType.rareChest, 1092)]
    [TestCase(ExpType.rareChestFirst, 1093)]
    [TestCase(ExpType.none, 0)]
    [TestCase(ExpType.rest, 0)]
    public void ReasonOfExpType(ExpType type, int reason) {
        Assert.That((int) type.Message(), Is.EqualTo(reason));
    }

    // The reason is an unsigned short between the gain and the total; 60001 does not fit a signed one.
    [TestCase(ExpMessageCode.monster, 0x61, 0xEA)]
    [TestCase(ExpMessageCode.quest, 0xF2, 0x03)]
    [TestCase(ExpMessageCode.miniGame, 0xBF, 0x0B)]
    [TestCase(ExpMessageCode.s_msg_take_hidden_map_exp, 0xBA, 0x0B)]
    [TestCase(ExpMessageCode.s_msg_take_exp, 0x00, 0x00)]
    public void ExpUpBytes(ExpMessageCode reason, byte low, byte high) {
        ByteWriter writer = ExperienceUpPacket.Add(1234, 99000, 50, reason, 7001);
        byte[] bytes = writer.Buffer[..writer.Length];
        Assert.Multiple(() => {
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 2 + 8 + 8 + 4 + 1));
            Assert.That(bytes[11], Is.EqualTo(low));
            Assert.That(bytes[12], Is.EqualTo(high));
        });

        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ExpUp));
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadLong(), Is.EqualTo(1234));
            Assert.That(reader.Read<ushort>(), Is.EqualTo((ushort) reason));
            Assert.That(reader.ReadLong(), Is.EqualTo(99000));
            Assert.That(reader.ReadLong(), Is.EqualTo(50));
            Assert.That(reader.ReadInt(), Is.EqualTo(7001));
            Assert.That(reader.ReadBool(), Is.False);
        });
    }

    // What an exp gain counts as for the adventure level is unchanged by the new reasons.
    [TestCase(ExpMessageCode.monster, ExpType.none)]
    [TestCase(ExpMessageCode.quest, ExpType.none)]
    [TestCase(ExpMessageCode.miniGame, ExpType.none)]
    [TestCase(ExpMessageCode.gathering, ExpType.none)]
    [TestCase(ExpMessageCode.s_msg_take_exp, ExpType.none)]
    [TestCase(ExpMessageCode.s_msg_take_map_exp, ExpType.mapCommon)]
    [TestCase(ExpMessageCode.s_msg_take_hidden_map_exp, ExpType.mapCommon)]
    [TestCase(ExpMessageCode.s_msg_take_fishing_exp, ExpType.fishing)]
    public void AdventureExpKind(ExpMessageCode reason, ExpType type) {
        Assert.That(reason.Type(), Is.EqualTo(type));
    }
}

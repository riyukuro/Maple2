using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Mastery 0xCD: CSceneStateGame::OnMastery (client/ms2/scenestategame.cpp:3920) reads the mode byte, then per mode.
public class MasteryLayoutTests {
    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mastery));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    // Mode 0: u8 type, i32 total mastery, i32 sub type.
    [Test]
    public void UpdateMasteryIsTypeTotalSubType() {
        ByteWriter writer = MasteryPacket.UpdateMastery(MasteryType.Mining, 1234);
        ByteReader reader = Open(writer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(reader.ReadInt(), Is.EqualTo(1234));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 1: i32 reward id, i32 n, n x (i32 item, i16 grade): UIRewardNotify::FUN_141abf6e0(id, grade).
    [Test]
    public void ClaimRewardIsIdCountThenItemGrade() {
        ByteWriter writer = MasteryPacket.ClaimReward(30301, new[] { new ItemComponent(20000001, 3, 5, ItemTag.None) });
        ByteReader reader = Open(writer, 1);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(30301));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
            Assert.That(reader.ReadShort(), Is.EqualTo(3));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 2: i16 type, i32 n, n x (i32 item, i16 grade).
    [Test]
    public void GetCraftedItemIsTypeCountThenItemGrade() {
        ByteWriter writer = MasteryPacket.GetCraftedItem(MasteryType.Cooking,
            new[] { new ItemComponent(20000002, 1, 2, ItemTag.None), new ItemComponent(20000003, 2, 1, ItemTag.None) });
        ByteReader reader = Open(writer, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadShort(), Is.EqualTo(10));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
            Assert.That(reader.ReadShort(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000003));
            Assert.That(reader.ReadShort(), Is.EqualTo(2));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 3: i16 result, CMasteryManager::FUN_140b6d710 (masterymanager.cpp:176): 1-5, 7 and 0xc have texts.
    [TestCase(MasteryError.s_mastery_error_lack_mastery, 1)]
    [TestCase(MasteryError.s_mastery_error_unknown, 5)]
    [TestCase(MasteryError.s_mastery_error_invalid_level, 7)]
    [TestCase(MasteryError.s_anti_addiction_cannot_receive, 0xc)]
    public void ErrorIsOneShort(MasteryError error, short value) {
        ByteWriter writer = MasteryPacket.Error(error);
        ByteReader reader = Open(writer, 3);
        Assert.That(reader.ReadShort(), Is.EqualTo(value));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}

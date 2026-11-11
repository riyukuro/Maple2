using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class DungeonRewardRulesTests {
    [TestCase(6, true, true, true, true)]
    [TestCase(6, true, true, false, false)] // the dungeon still runs
    [TestCase(6, true, false, true, false)] // the room does not leave on close
    [TestCase(6, false, true, true, false)] // not in a dungeon
    [TestCase(5, true, true, true, false)] // give up: not answered
    [TestCase(0, true, true, true, false)]
    public void Leaves(byte mode, bool inDungeon, bool leaveAfterCloseReward, bool ended, bool expected) {
        Assert.That(DungeonRewardRules.Leaves(mode, inDungeon, leaveAfterCloseReward, ended), Is.EqualTo(expected));
    }

    // The packet is the mode byte alone.
    [Test]
    public void Bytes() {
        var writer = new ByteWriter();
        writer.WriteByte(DungeonRewardRules.Leave);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

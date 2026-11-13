using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class DungeonRewardRulesTests {
    [TestCase(6, true, true, true, true)]
    [TestCase(6, true, true, false, false)] // the dungeon still runs
    [TestCase(6, true, false, true, false)] // the room does not leave on close
    [TestCase(6, false, true, true, false)] // not in a dungeon
    [TestCase(5, true, true, true, false)] // give up is not a leave
    [TestCase(0, true, true, true, false)]
    public void Leaves(byte mode, bool inDungeon, bool leaveAfterCloseReward, bool ended, bool expected) {
        Assert.That(DungeonRewardRules.Leaves(mode, inDungeon, leaveAfterCloseReward, ended), Is.EqualTo(expected));
    }

    [TestCase(5, true, true, false, true, DungeonRewardRules.GiveUpResult.GiveUp)]
    [TestCase(5, true, true, false, false, DungeonRewardRules.GiveUpResult.TooEarly)] // the script has not allowed it yet
    [TestCase(5, true, false, false, true, DungeonRewardRules.GiveUpResult.Ignored)] // not the party leader
    [TestCase(5, true, true, true, true, DungeonRewardRules.GiveUpResult.Ignored)] // the room has its result
    [TestCase(5, false, true, false, true, DungeonRewardRules.GiveUpResult.Ignored)] // not in a dungeon room
    [TestCase(6, true, true, false, true, DungeonRewardRules.GiveUpResult.Ignored)]
    public void GiveUpOf(byte mode, bool inDungeonRoom, bool isLeader, bool ended, bool enabled, DungeonRewardRules.GiveUpResult expected) {
        Assert.That(DungeonRewardRules.GiveUpOf(mode, inDungeonRoom, isLeader, ended, enabled), Is.EqualTo(expected));
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

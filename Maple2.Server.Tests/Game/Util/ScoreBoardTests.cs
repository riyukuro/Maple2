using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class ScoreBoardTests {
    [Test]
    public void ShadowGauge_IsBossGauge() {
        Assert.That(ScoreBoard.Kind("ShadowGauge", 800), Is.EqualTo((byte) 1));
    }

    [Test]
    public void ShadowGauge_WithoutMost_IsNone() {
        Assert.That(ScoreBoard.Kind("ShadowGauge", 0), Is.Null);
    }

    [Test]
    public void TopCenter_IsTopCenterScore() {
        Assert.That(ScoreBoard.Kind("ScoreBoardTopCenter", 0), Is.EqualTo((byte) 2));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("ScoreBoardTopRight")]
    public void OtherTypes_AreNone(string? type) {
        Assert.That(ScoreBoard.Kind(type, 240), Is.Null);
    }

    [Test]
    public void Enter_NoBoard_SendsNothing() {
        Assert.That(ScoreBoard.EnterPacket(null), Is.Null);
    }

    [Test]
    public void Enter_OpenBoard_SendsOpenAtCurrentPoints() {
        var board = new ScoreBoard.Board(2, "", 0, 0) with { Points = 7 };
        ByteWriter? writer = ScoreBoard.EnterPacket(board);
        Assert.That(writer, Is.Not.Null);
        var reader = new ByteReader(writer!.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ShadowExpedition));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadString(), Is.EqualTo(""));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 2 + 4 + 4));
    }

    [Test]
    public void Enter_BossGauge_KeepsTitleAndMost() {
        ByteWriter? writer = ScoreBoard.EnterPacket(new ScoreBoard.Board(1, "ab", 0, 800));
        var reader = new ByteReader(writer!.Buffer, 0);
        reader.Read<SendOp>();
        reader.ReadByte();
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(800));
    }
}

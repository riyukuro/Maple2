using System.Collections.Generic;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// PlayArcade against CSceneStateGame::OnPlayArcade (scenestategame.cpp, the mode byte first, 0: int type; 1: int score;
// 2: the dark stream result reader; 12: u8 align; 13: none) and the dialogs it forwards the mode to (life bar, couple
// dance, text effect, arcade lobby). HideAndSeek against CSceneStateGame::OnHideAndSeek (scenestategame.cpp:5200 on):
// u8 mode; 0: int object id, HideAndSeek::Info::FUN_14050a320 (commonfiles/hideandseek.cpp:121: bool, bool, bool, long
// exp, int kills, u8 team, int when team is 1); 1: u8 count x u8; 2: int id, u16 type, int x3; 3: int id; 4: int id, int
// x2; 5 and 6: int id, int skill set; 7: int id, int, bool, bool; 8: int id, int round, int item, int, int count.
public class ArcadeHideSeekLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void Arcade_ScoreOpenLife() {
        ByteReader reader = Read(ArcadePacket.Open(3), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.Score(900), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(900));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.LifeInit(1, 5, 6), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.Life(4, 6), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Arcade_ButtonsStreakScoresBoard() {
        ByteReader reader = Read(ArcadePacket.Buttons(true, 7), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.Buttons(false), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.Streak(3), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.Scores(11, [new ArcadeBest(2, 50, 0), new ArcadeBest(4, 80, 0)]), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(9));
        Assert.That(reader.ReadInt(), Is.EqualTo(11));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 2, 50, 4, 80 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.ScoreBoard(2), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(12));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ArcadePacket.CloseScore(), SendOp.PlayArcade);
        Assert.That(reader.ReadByte(), Is.EqualTo(13));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void HideAndSeek_InfoResultAndTheShortModes() {
        ByteReader reader = Read(HideAndSeekPacket.Info(5, true, false, 900, 2, 1, 33), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(new[] { reader.ReadBool(), reader.ReadBool(), reader.ReadBool() }, Is.EqualTo(new[] { true, false, false }));
        Assert.That(reader.ReadLong(), Is.EqualTo(900));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(33));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.Info(5, false, true, 0, 0, 2), SendOp.HideAndSeek);
        reader.ReadByte();
        reader.ReadInt();
        reader.ReadBool();
        reader.ReadBool();
        reader.ReadBool();
        reader.ReadLong();
        reader.ReadInt();
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.HideProps(new List<byte> { 4, 9 }), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 4, 9 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.LifeBar(5, 2, 1, 4, 6), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadShort(), Is.EqualTo(2));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 1, 4, 6 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.End(5), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.Life(5, 4, 6), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 5, 4, 6 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.AddSkillSet(5, 77), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 5, 77 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.RemoveSkillSet(5, 77), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 5, 77 }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.Watch(5, 8, true, false), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 5, 8 }));
        Assert.That(new[] { reader.ReadBool(), reader.ReadBool() }, Is.EqualTo(new[] { true, false }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(HideAndSeekPacket.Result(5, 2, 20000001, 3), SendOp.HideAndSeek);
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt() },
            Is.EqualTo(new[] { 5, 2, 20000001, 0, 3 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

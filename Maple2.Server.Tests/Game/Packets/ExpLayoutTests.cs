using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ExpUp (CSceneStateGame::OnExpUp, scenestategame.cpp:4129): byte mode; 0: long exp, short type, long total,
// long rest, int npc object id, bool; 1: long rest.
public class ExpLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.ExpUp));
        return reader;
    }

    [Test]
    public void AddIsExpTypeTotalRestNpc() {
        ByteReader reader = Read(ExperienceUpPacket.Add(120, 5000, 300, ExpMessageCode.s_msg_take_exp, 42, true));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(120));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) ExpMessageCode.s_msg_take_exp));
        Assert.That(reader.ReadLong(), Is.EqualTo(5000));
        Assert.That(reader.ReadLong(), Is.EqualTo(300));
        Assert.That(reader.ReadInt(), Is.EqualTo(42));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void SetRestExpIsRest() {
        ByteReader reader = Read(ExperienceUpPacket.SetRestExp(777));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(777));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

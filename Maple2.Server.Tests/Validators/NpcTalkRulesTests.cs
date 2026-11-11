using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class NpcTalkRulesTests {
    // ResolvePenalty and TakeBoat: i32 npc object id, nothing after it.
    [Test]
    public void NpcBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(0x11223344);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(NpcTalkRules.ReadNpc(reader), Is.EqualTo(0x11223344));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(500, 500, 81, true)]
    [TestCase(0, 500, 81, false)] // no talk open
    [TestCase(501, 500, 81, false)] // a talk with another npc
    [TestCase(500, 500, 82, false)] // not a doctor
    [TestCase(0, 0, 81, false)]
    public void PenaltyResolve(int talkNpc, int npc, int kind, bool expected) {
        Assert.That(NpcTalkRules.PenaltyResolveOpen(talkNpc, npc, kind), Is.EqualTo(expected));
    }
}

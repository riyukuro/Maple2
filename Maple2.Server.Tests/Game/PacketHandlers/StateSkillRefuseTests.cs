using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// StateSkill 0 that the server does not take (here an item the player does not hold) is answered with
// SkillUseFailed 6 naming the state, so the client leaves it (CSceneStateGame::OnSkillUseFailed).
public class StateSkillRefuseTests {
    [Test]
    public void UnknownItem_AnswersStateFail() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(ConfigManager).GetField("skillCooldowns", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Config, new Dictionary<int, SkillCooldown>());
        test.TakeSent();

        new StateSkillHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(77);
            writer.WriteInt(0);
            writer.WriteInt(10500061);
            writer.WriteShort(1);
            writer.WriteInt((int) ActorState.Climb);
            writer.WriteInt(0);
            writer.WriteLong(12345);
        }));

        byte[] expected = SkillUseFailedPacket.StateFail(77, 10500061, (int) ActorState.Climb, 0f, null, 0).ToArray();
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.SkillUseFailed));
        // The cooldown start tick depends on the field tick; the rest is fixed.
        Assert.That(sent[0][..20], Is.EqualTo(expected[..20]));
        Assert.That(sent[0].Length, Is.EqualTo(30));
    }
}

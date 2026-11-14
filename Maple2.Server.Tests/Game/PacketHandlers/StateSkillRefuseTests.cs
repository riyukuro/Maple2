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

    // The quick slot's item (CSkillSystem::FUN_141764e30): held, at a level it allows; a glide takes only a glider
    // (type 5, CItemData::GetGlideItemId). Before, a glide took any held item.
    [TestCase(StateSkillHandler.GliderType, 0, ActorState.Glide, ExpectedResult = true)]
    [TestCase(2, 0, ActorState.Glide, ExpectedResult = false)]
    [TestCase(StateSkillHandler.GliderType, 60, ActorState.Glide, ExpectedResult = false)]
    [TestCase(2, 0, ActorState.Climb, ExpectedResult = true)]
    [TestCase(2, 60, ActorState.Climb, ExpectedResult = false)]
    public bool StateItemAllowed_TheQuickSlotItem(int type, int level, ActorState state) {
        Maple2.Model.Metadata.ItemMetadata metadata = TestGameSession.Metadata(50200001, type: type);
        var item = new Item(metadata with { Limit = metadata.Limit with { Level = level } });
        return StateSkillHandler.StateItemAllowed(item, state, 50);
    }

    [Test]
    public void Glide_WithAHeldItemThatIsNoGlider_AnswersStateFail() {
        var test = new TestGameSession();
        Item held = test.Store(TestGameSession.Metadata(20000001, type: 2));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(ConfigManager).GetField("skillCooldowns", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Config, new Dictionary<int, SkillCooldown>());
        test.TakeSent();

        new StateSkillHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(78);
            writer.WriteInt(0);
            writer.WriteInt(10500061);
            writer.WriteShort(1);
            writer.WriteInt((int) ActorState.Glide);
            writer.WriteInt(0);
            writer.WriteLong(held.Uid);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.SkillUseFailed));
        Assert.That(session.Item.Inventory.Get(held.Uid), Is.Not.Null);
    }
}

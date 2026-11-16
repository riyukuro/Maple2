using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// A skill whose begin condition names a spirit stat (skill basic condition stat sp) is cast only with that much
// spirit, as the client tests it for every skill (FUN_14176a8c0): 30 asked, 10 held is refused, 30 held casts. The ids
// are made up.
public class SkillSpiritThresholdTests : TriggerTestBase {
    private const int SkillId = 90000965;

    [TestCase(10L, false)]
    [TestCase(30L, true)]
    [TestCase(80L, true)]
    public void ASpiritThreshold_IsMetByTheSpiritHeld(long spirit, bool runs) {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(SkillId);
        BeginCondition condition = skill.Data.Condition with {
            Stat = new Dictionary<BasicAttribute, long> { [BasicAttribute.Spirit] = 30 },
        };
        QuestConditionWiringTests.SetSkills(test, skill with { Data = skill.Data with { Condition = condition } });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        test.Session.Player.Stats.Values[BasicAttribute.Spirit].Current = spirit;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        test.TakeSent();

        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(SkillId);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(0);
            writer.WriteLong(0);
        }));

        Assert.That(test.Session.Player.ActiveSkills.Get(1) != null, Is.EqualTo(runs));
    }
}

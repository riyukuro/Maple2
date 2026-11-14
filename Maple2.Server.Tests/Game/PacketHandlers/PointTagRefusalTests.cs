using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An attack point packet whose tag links more tags than the arrow bounces is dropped whole: the cast's position and
// direction stay where the last taken point put them. Before, they were set before the tags were read, so a refused
// point still moved the cast. The skill id is made up.
public class PointTagRefusalTests : TriggerTestBase {
    private const int SkillId = 90000951;
    private const long CastUid = 5;

    [SetUp]
    public void SetUp() {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SkillMetadata cast = QuestConditionWiringTests.CastSkill(SkillId);
        // One attack point, an arrow of no bounces: the client sends no linked tag for it.
        var arrow = (SkillMetadataArrow) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataArrow)) with { BounceCount = 0 };
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with { Arrow = arrow, TargetCount = 1 };
        SkillMetadata skill = cast with { Data = cast.Data with { Motions = [cast.Data.Motions[0] with { Attacks = [attack] }] } };
        QuestConditionWiringTests.SetSkills(test, skill);
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(CastUid);
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
        Assert.That(test.Session.Player.ActiveSkills.Get(CastUid), Is.Not.Null, "the cast runs");
        test.TakeSent();
    }

    // The attack point packet (Skill 1 0) at the player's position, facing x, with one tag and links linked tags.
    private void Point(int links) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteLong(CastUid);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitX);
            writer.WriteByte(1);
            writer.WriteInt(0);
            writer.WriteLong(100);
            writer.WriteInt(1); // a target named, so no corpse search runs
            writer.WriteByte(0);
            for (int i = 0; i < links; i++) {
                writer.WriteBool(true);
                writer.WriteLong(101 + i);
                writer.WriteInt(0);
                writer.WriteByte(0);
                writer.WriteByte(0);
            }
            writer.WriteBool(false);
        }));
    }

    [TestCase(0, true)]
    [TestCase(1, false)]
    public void ARefusedPointLeavesTheCastsDirection(int links, bool taken) {
        SkillRecord record = test.Session.Player.ActiveSkills.Get(CastUid)!;
        Vector3 before = record.Direction;
        Assert.That(before, Is.Not.EqualTo(Vector3.UnitX));

        Point(links);

        Assert.That(record.Direction, Is.EqualTo(taken ? Vector3.UnitX : before));
    }
}

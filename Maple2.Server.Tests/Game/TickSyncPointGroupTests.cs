using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The client sends a tick sync (Skill sub 3, CSkillRecord::SendSkill) only at a loop end of a held motion
// (stateskill.cpp:867-927), and each one makes it forget what the cast's point groups hit. Before, the server forgot
// them at any sync, so a changed client hit an object again with every attack point of a point group (10100031,
// 10900101 and others, none of them held) by sending syncs between its target packets. The skill id is made up.
public class TickSyncPointGroupTests : TriggerTestBase {
    private const int Skill = 90000911;
    private static readonly SkillHitShape GroupShape = new(Arrow: false, Targeted: false, Max: 5, Returns: false, Repierce: false, PointGroup: 1);

    [SetUp]
    public void SetUp() {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        QuestConditionWiringTests.SetSkills(test, QuestConditionWiringTests.CastSkill(Skill));
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        test.TakeSent();
    }

    private SkillHandler Handler => new() { SkillMetadata = Field.SkillMetadata, TableMetadata = null! };

    private void Cast() {
        Handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(Skill);
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
    }

    private void TickSync() {
        Handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(1);
            writer.WriteInt((int) Field.FieldTick);
        }));
    }

    // One attack of the point group's attack point 0 that hits object 101: whether it pays.
    private static bool Hit(SkillRecord record, long attackUid) {
        record.Hits.Declare(attackUid, 0, 0);
        return record.Hits.TakeTarget(attackUid, 0, 0) && record.Hits.TakeHit(attackUid, 101, 0, GroupShape) == HitRefusal.None;
    }

    [Test]
    public void ASyncForAMotionThatDoesNotLoop_KeepsThePointGroupsHits() {
        Cast();
        SkillRecord? record = test.Session.Player.ActiveSkills.Get(1);
        Assert.That(record, Is.Not.Null, "the cast runs");
        Assert.That(record!.Hits.TakePoint(0, 0, held: false, count: 5, tick: Field.FieldTick), Is.True);
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        TickSync();
        Assert.Multiple(() => {
            Assert.That(Hit(record, 2), Is.False, "the group already hit the object");
            Assert.That(test.Session.Player.ActiveSkills.Get(1), Is.Not.Null, "the sync drops nothing else");
        });
    }
}

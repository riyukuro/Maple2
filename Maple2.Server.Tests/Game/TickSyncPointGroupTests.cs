using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
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
    private const string Sequence = "Attack_Group_A";
    private static readonly SkillHitShape GroupShape = new(Arrow: false, Targeted: false, Max: 5, Returns: false, Repierce: false, PointGroup: 1);

    [SetUp]
    public void SetUp() {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(Skill);
        SkillMetadataMotion motion = skill.Data.Motions[0];
        QuestConditionWiringTests.SetSkills(test, skill with {
            Data = skill.Data with { Motions = [motion with { MotionProperty = motion.MotionProperty with { SequenceName = Sequence } }] },
        });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        test.TakeSent();
    }

    // The player's rig: the skill's sequence without a loop, and the other sequences named.
    private void Rig(bool hasSequence, params AnimationSequenceMetadata[] others) {
        var sequences = others.ToDictionary(sequence => sequence.Name);
        if (hasSequence && !sequences.ContainsKey(Sequence)) {
            sequences[Sequence] = new AnimationSequenceMetadata(Sequence, 1, 1f, [new AnimationKey("hit", 0.2f), new AnimationKey("end", 1f)]);
        }
        typeof(AnimationManager).GetField(nameof(AnimationManager.RigMetadata))!
            .SetValue(test.Session.Player.Animation, new AnimationMetadata("male", sequences));
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
        Rig(hasSequence: true);
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

    // A motion with a loop count sends a sync at each loop end with no continue (stateskill.cpp:875, m_nRepeat). Its
    // loop is not known when the rig lacks the sequence, or has a prefixed one of it that loops (the client plays the
    // name with its actor's prefix, animationmanager.cpp:209-230): the sync is taken, never stricter than the client.
    [TestCase(false)]
    [TestCase(true)]
    public void ASyncForAMotionWhoseLoopIsNotKnown_ForgetsThePointGroupsHits(bool prefixedLoop) {
        if (prefixedLoop) {
            Rig(hasSequence: true, new AnimationSequenceMetadata("Fox_" + Sequence, 2, 2f,
                [new AnimationKey("loopstart", 0.5f), new AnimationKey("loopend", 1.5f)]));
        } else {
            Rig(hasSequence: false);
        }
        Cast();
        SkillRecord? record = test.Session.Player.ActiveSkills.Get(1);
        Assert.That(record, Is.Not.Null, "the cast runs");
        Assert.That(record!.Hits.TakePoint(0, 0, held: false, count: 5, tick: Field.FieldTick), Is.True);
        Assert.That(Hit(record, 1), Is.True, "the first hit pays");
        TickSync();
        Assert.That(Hit(record, 2), Is.True, "the sync forgot the group's hit");
    }

    // The base sequence loops for 5 s and a prefixed one of it for 0.5 s: the client may loop the prefixed one, so
    // its syncs come every 0.5 s; they are not spaced by the base's loop (which would refuse them once the lag
    // budget runs out).
    [Test]
    public void ASyncForAShorterPrefixedLoop_IsNotSpacedByTheBaseLoop() {
        Rig(hasSequence: true,
            new AnimationSequenceMetadata(Sequence, 1, 6f, [new AnimationKey("loopstart", 0.5f), new AnimationKey("loopend", 5.5f)]),
            new AnimationSequenceMetadata("Fox_" + Sequence, 2, 2f, [new AnimationKey("loopstart", 0.5f), new AnimationKey("loopend", 1f)]));
        Cast();
        SkillRecord? record = test.Session.Player.ActiveSkills.Get(1);
        Assert.That(record, Is.Not.Null, "the cast runs");
        Assert.That(record!.Hits.TakePoint(0, 0, held: false, count: 5, tick: Field.FieldTick), Is.True);
        for (int i = 0; i < 5; i++) {
            Assert.That(Hit(record, 1 + i), Is.True, $"hit {i} after {i} syncs");
            TickSync();
        }
    }
}

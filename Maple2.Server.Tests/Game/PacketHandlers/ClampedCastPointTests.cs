using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A cast sent more than 600 away from the player right after a position sync is stood at the player's position
// (SkillHitRules.CastTooFar). The client's attack point packets still carry the cast position it sent (StateSkill
// m_ptPos, stateskill.cpp:1376), so they are held to that one; before, they were held to the moved cast and every
// point of such a cast was dropped, with all its hits. The skill id is made up.
public class ClampedCastPointTests : TriggerTestBase {
    private const int SkillId = 90000954;
    private const long CastUid = 5;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Vector3 Away = new(0, 700, 0);

    [SetUp]
    public void SetUp() {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SkillMetadata cast = QuestConditionWiringTests.CastSkill(SkillId);
        var arrow = (SkillMetadataArrow) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataArrow));
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            Arrow = arrow, TargetCount = 1,
            Range = (SkillMetadataRange) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataRange)),
        };
        QuestConditionWiringTests.SetSkills(test, cast with { Data = cast.Data with { Motions = [cast.Data.Motions[0] with { Attacks = [attack] }] } });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldSkill>());
        typeof(FieldManager).GetField("removedSkills", Any)!.SetValue(Field, new ConcurrentDictionary<int, (FieldSkill, long)>());
        // A position sync just came in.
        typeof(FieldPlayer).GetProperty("StateSyncTrackingTick", Any)!.SetValue(test.Session.Player, Field.FieldTick);
    }

    private void Handle(System.Action<Maple2.PacketLib.Tools.ByteWriter> write) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(write));
    }

    private SkillRecord CastAt(Vector3 position) {
        Handle(writer => {
            writer.WriteByte(0);
            writer.WriteLong(CastUid);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(SkillId);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(0);
            writer.WriteLong(0);
        });
        SkillRecord? record = test.Session.Player.ActiveSkills.Get(CastUid);
        Assert.That(record, Is.Not.Null, "the cast runs");
        return record!;
    }

    // The attack point packet (Skill 1 0) at the position given, declaring object 100 as attack 0's target.
    private void PointAt(Vector3 position) {
        Handle(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteLong(CastUid);
            writer.WriteByte(0);
            writer.Write(position);
            writer.Write(Vector3.UnitY);
            writer.WriteByte(1);
            writer.WriteInt(0);
            writer.WriteLong(100);
            writer.WriteInt(1);
            writer.WriteByte(0);
            writer.WriteBool(false);
        });
    }

    // The target packet (Skill 1 1) of attack 100 hitting nothing, at the player; TargetUid tells whether it was taken,
    // which it is only when the attack point declared it.
    private bool Target(SkillRecord record) {
        record.TargetUid = 0;
        Handle(writer => {
            writer.WriteByte(1);
            writer.WriteByte(1);
            writer.WriteLong(CastUid);
            writer.WriteLong(100);
            writer.Write(test.Session.Player.Position);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteInt(0);
        });
        return record.TargetUid == 100;
    }

    [Test]
    public void APointAtTheSentCastPosition_IsTakenAndTheCastStaysAtThePlayer() {
        Vector3 player = test.Session.Player.Position;
        SkillRecord record = CastAt(player + Away);
        Assert.That(record.CastPosition, Is.EqualTo(player), "the cast is stood at the player");
        PointAt(player + Away);
        Assert.Multiple(() => {
            Assert.That(Target(record), Is.True, "the point is taken and declares its target");
            Assert.That(record.Position, Is.EqualTo(player), "the cast does not move to the sent position");
        });
    }

    // A point at the moved cast, away from what the client sent, is not one the client sends.
    [Test]
    public void APointAtTheMovedCastPosition_IsDropped() {
        Vector3 player = test.Session.Player.Position;
        SkillRecord record = CastAt(player + Away);
        PointAt(player);
        Assert.That(Target(record), Is.False);
    }

    // A cast that is not moved holds its points to its own position, as before.
    [Test]
    public void ACastNearThePlayer_HoldsItsPointsToItsPosition() {
        Vector3 player = test.Session.Player.Position;
        SkillRecord record = CastAt(player + new Vector3(0, 100, 0));
        PointAt(player + new Vector3(0, 100.5f, 0));
        Assert.Multiple(() => {
            Assert.That(Target(record), Is.True);
            Assert.That(record.Position, Is.EqualTo(player + new Vector3(0, 100.5f, 0)));
        });
    }
}

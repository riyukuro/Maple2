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

// A magic sends its target and region path packets when it meets something or ends (CMagic, magicdata.cpp:1846, 1956,
// 2402), with its cast's id, however many casts the player made since and however long its path flies. Before, the
// server looked the cast up among the 3 pending ones only, and took these packets only within 5 s of the cast, so a
// quick caster's slow magic and a path longer than the window paid nothing. The skill id is made up.
public class LateMagicHitTests : TriggerTestBase {
    private const int SkillId = 90000953;
    private const long CastUid = 5;
    private const long MagicPathId = 7;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private long start;

    // The cast's magic path flies lifeTime seconds.
    private void Cast(float lifeTime) {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SkillMetadata cast = QuestConditionWiringTests.CastSkill(SkillId);
        var arrow = (SkillMetadataArrow) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataArrow)) with { BounceCount = 0 };
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            Arrow = arrow, TargetCount = 1, MagicPathId = MagicPathId,
            Range = (SkillMetadataRange) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataRange)),
        };
        SkillMetadata skill = cast with { Data = cast.Data with { Motions = [cast.Data.Motions[0] with { Attacks = [attack] }] } };
        QuestConditionWiringTests.SetSkills(test, skill);
        var path = (MagicPath) RuntimeHelpers.GetUninitializedObject(typeof(MagicPath)) with { LifeTime = lifeTime };
        test.SetTable(new MagicPathTable(new Dictionary<long, IReadOnlyList<MagicPath>> { [MagicPathId] = [path] }));
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldSkill>());
        typeof(FieldManager).GetField("removedSkills", Any)!.SetValue(Field, new ConcurrentDictionary<int, (FieldSkill, long)>());
        start = Field.FieldTick;
        Handle(writer => {
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
        });
        Assert.That(test.Session.Player.ActiveSkills.Get(CastUid), Is.Not.Null, "the cast runs");
        // The attack point packet (Skill 1 0) fires one arrow, attack 100, arrow index 0.
        Handle(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteLong(CastUid);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.WriteByte(1);
            writer.WriteInt(0);
            writer.WriteLong(100);
            writer.WriteInt(1);
            writer.WriteByte(0);
            writer.WriteBool(false);
        });
        test.TakeSent();
    }

    private void Handle(System.Action<Maple2.PacketLib.Tools.ByteWriter> write) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(write));
    }

    private void SetTick(long tick) {
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(Field, tick);
    }

    // Three more casts of the skill: the pending queue holds those three only.
    private void CastThreeMore() {
        SkillQueue queue = test.Session.Player.ActiveSkills;
        SkillRecord record = queue.Get(CastUid)!;
        for (long uid = CastUid + 1; uid <= CastUid + 3; uid++) {
            var later = new SkillRecord(record.Metadata, uid, test.Session.Player);
            later.Hits.Open(Field.FieldTick);
            queue.Add(later, Field.FieldTick);
        }
        Assert.That(queue.Get(CastUid), Is.Null, "no longer pending");
    }

    // The target packet (Skill 1 1) of attack 100 with no object hit; the cast's TargetUid tells whether it was taken.
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

    // The region path packet (Skill 1 2) of arrow 0 ending 50 ahead; the cast's position tells whether it was taken.
    private bool RegionPath(SkillRecord record) {
        Vector3 end = test.Session.Player.Position + new Vector3(0, 50, 0);
        record.Position = test.Session.Player.Position;
        Handle(writer => {
            writer.WriteByte(1);
            writer.WriteByte(2);
            writer.WriteLong(CastUid);
            writer.WriteByte(0);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.Write(end);
            writer.Write(Vector3.Zero);
        });
        return record.Position == end;
    }

    [Test]
    public void ATargetPacketOfACastThreeCastsBackIsTaken() {
        Cast(1);
        SkillRecord record = test.Session.Player.ActiveSkills.Get(CastUid)!;
        CastThreeMore();
        Assert.That(Target(record), Is.True);
    }

    [Test]
    public void ARegionPathOfACastThreeCastsBackIsTaken() {
        Cast(1);
        SkillRecord record = test.Session.Player.ActiveSkills.Get(CastUid)!;
        CastThreeMore();
        Assert.That(RegionPath(record), Is.True);
    }

    // A path that flies 3 s: its packets are taken until 8 s after the cast (the 5 s window, then the flight), not after.
    [TestCase(7000, ExpectedResult = true)]
    [TestCase(8100, ExpectedResult = false)]
    public bool ATargetPacketPastTheWindowIsTakenWithinTheFlight(long sinceCast) {
        Cast(3);
        SkillRecord record = test.Session.Player.ActiveSkills.Get(CastUid)!;
        SetTick(start + sinceCast);
        return Target(record);
    }

    [TestCase(7000, ExpectedResult = true)]
    [TestCase(8100, ExpectedResult = false)]
    public bool ARegionPathPastTheWindowIsTakenWithinTheFlight(long sinceCast) {
        Cast(3);
        SkillRecord record = test.Session.Player.ActiveSkills.Get(CastUid)!;
        SetTick(start + sinceCast);
        return RegionPath(record);
    }

    // A cast past its keep time is not looked up: the late packet is still dropped.
    [Test]
    public void ACastPastItsKeepTimeIsNotTaken() {
        Cast(1);
        SkillRecord record = test.Session.Player.ActiveSkills.Get(CastUid)!;
        CastThreeMore();
        SetTick(start + 6100);
        Assert.That(Target(record), Is.False);
    }
}

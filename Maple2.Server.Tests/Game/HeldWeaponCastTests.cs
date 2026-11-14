using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// The client's cast check answers 0x20 and 0x21 through the skill handler (SkillCastRules.GuildPvpAwakeningRefuses,
// HeldWeaponRefuses) and the release of a held object weapon when a skill starts. The ids are made up.
public class HeldWeaponCastTests : TriggerTestBase {
    private const int WeaponSkill = 90000921, OtherWeaponSkill = 90000922, Plain = 90000923, Keeps = 90000924;
    private const int Awakened = 90000925, AwakenedSub = 90000926;

    private static SkillMetadata Skill(int id, int mainType = 0, bool release = true) {
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(id);
        return skill with { Property = skill.Property with { MainType = mainType, ReleaseObjectWeapon = release } };
    }

    private void Enter(InstanceType instance = InstanceType.none) {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        QuestConditionWiringTests.SetSkills(test, Skill(WeaponSkill, mainType: 4), Skill(OtherWeaponSkill, mainType: 4), Skill(Plain), Skill(Keeps, release: false),
            Skill(Awakened), Skill(AwakenedSub));
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(null!, new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Awakening] = [new JobTable.Skill(Awakened, [AwakenedSub], 1, 0)],
            }, []),
        }));
        typeof(FieldManager).GetProperty(nameof(FieldManager.FieldInstance))!
            .SetValue(Field, new InstanceFieldMetadata(Field.MapId, instance, 0, false, 0, true, 0, 0, 0, 0));
        test.TakeSent();
    }

    private void Hold(int skillId, short level = 1) => test.Session.HeldLiftup = new LiftupWeapon(null!, 50000921, skillId, level);

    // True when the cast was taken.
    private bool Cast(int skillId, long uid) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(uid);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(skillId);
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
        return test.Session.Player.ActiveSkills.Get(uid) != null;
    }

    [Test]
    public void AWeaponSkill_IsNotTakenWithoutItsWeapon() {
        Enter();
        Assert.That(Cast(WeaponSkill, 1), Is.False);
        Hold(OtherWeaponSkill);
        Assert.Multiple(() => {
            Assert.That(Cast(WeaponSkill, 2), Is.False);
            Assert.That(test.Session.HeldLiftup, Is.Not.Null);
        });
    }

    // The client compares the skill id only, not the level.
    [Test]
    public void AWeaponSkill_IsTakenWithItsWeapon_AndLetsItGo() {
        Enter();
        Hold(WeaponSkill, level: 3);
        Assert.Multiple(() => {
            Assert.That(Cast(WeaponSkill, 1), Is.True);
            Assert.That(test.Session.HeldLiftup, Is.Null);
        });
    }

    // The client refuses no other skill while a weapon is held; one that releases it lets it go.
    [Test]
    public void AnotherSkill_IsTakenWhileHolding() {
        Enter();
        Hold(WeaponSkill);
        Assert.Multiple(() => {
            Assert.That(Cast(Keeps, 1), Is.True);
            Assert.That(test.Session.HeldLiftup, Is.Not.Null);
            Assert.That(Cast(Plain, 2), Is.True);
            Assert.That(test.Session.HeldLiftup, Is.Null);
        });
    }

    [Test]
    public void AnAwakeningSkill_IsNotTakenOnAGuildPvpField() {
        Enter(InstanceType.GuildPvp);
        Assert.Multiple(() => {
            Assert.That(Cast(Awakened, 1), Is.False);
            Assert.That(Cast(AwakenedSub, 2), Is.False);
            Assert.That(Cast(Plain, 3), Is.True);
        });
    }

    [Test]
    public void AnAwakeningSkill_IsTakenElsewhere() {
        Enter(InstanceType.GuildVsGame);
        Assert.That(Cast(Awakened, 1), Is.True);
    }
}

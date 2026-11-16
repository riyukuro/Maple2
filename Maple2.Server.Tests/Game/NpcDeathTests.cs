using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;

namespace Maple2.Server.Tests.Game;

// An npc's death and its corpse, on the cast fixture: a lethal hit kills it and tells the field once, the body leaves
// after Dead.Time, and a hittable corpse stays, announces itself every second and answers each hit with a corpse-hit.
public class NpcDeathTests : NpcCastFixture {
    private static readonly string[] Rig = ["Idle_A", "Attack_Idle_A", "Dead_A"];

    private void Kill() {
        var property = (SkillMetadataProperty) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(1, 1, null, property, null!, null!);
        SkillMetadataAttack attack = Attack("p0") with {
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 1_000_000,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        npc.ApplyDamage(test.Session.Player, new DamageRecord(skill, attack), attack);
        // The death is taken at the npc's next update.
        npc.Update(System.Environment.TickCount64);
    }

    private void Hit() {
        var property = (SkillMetadataProperty) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(1, 1, null, property, null!, null!);
        SkillMetadataAttack attack = Attack("p0");
        npc.ApplyDamage(test.Session.Player, new DamageRecord(skill, attack), attack);
    }

    private static NpcMetadata WithCorpse(bool hitAble) {
        NpcMetadata metadata = AiStateTests.Npc(CasterId);
        return metadata with { Corpse = new NpcMetadataCorpse(50, 50, 50, 0, 0, "", hitAble, Vector3.Zero) };
    }

    [Test]
    public void LethalHit_KillsTheNpc_AndTellsTheFieldOnce() {
        StartCaster(Rig);
        int queued = Field.Scheduler.Count;

        Kill();
        int dead = Sent(SendOp.NpcControl, out _);
        npc.Update(Field.FieldTick + 5000);

        Assert.Multiple(() => {
            Assert.That(npc.IsDead, Is.True);
            Assert.That(npc.IsCorpse, Is.False);
            Assert.That(dead, Is.GreaterThanOrEqualTo(1));
            Assert.That(Sent(SendOp.NpcControl, out _), Is.EqualTo(0), "no corpse: a dead npc says nothing more");
            Assert.That(Field.Scheduler.Count, Is.GreaterThan(queued), "its removal is queued");
        });
    }

    [Test]
    public void HittableCorpse_StaysAndAnnouncesEverySecond() {
        CasterMetadata = WithCorpse(true);
        StartCaster(Rig);

        Kill();
        test.TakeSent();
        long now = System.Environment.TickCount64;
        npc.Update(now + 500);
        int early = Sent(SendOp.NpcControl, out _);
        npc.Update(now + 1500);
        int due = Sent(SendOp.NpcControl, out _);

        Assert.Multiple(() => {
            Assert.That(npc.IsCorpse, Is.True);
            Assert.That(early, Is.EqualTo(0));
            Assert.That(due, Is.EqualTo(1));
        });
    }

    // A hit on the corpse changes nothing but answers with the corpse-hit control entry.
    [Test]
    public void HitOnTheCorpse_SendsACorpseHit() {
        CasterMetadata = WithCorpse(true);
        StartCaster(Rig);
        Kill();
        test.TakeSent();

        Hit();

        Assert.Multiple(() => {
            Assert.That(npc.IsCorpse, Is.True);
            Assert.That(Sent(SendOp.NpcControl, out _), Is.EqualTo(1));
        });
    }

    [Test]
    public void CorpseThatIsNotHittable_IsNoCorpse() {
        CasterMetadata = WithCorpse(false);
        StartCaster(Rig);

        Kill();

        Assert.That(npc.IsCorpse, Is.False);
    }
}

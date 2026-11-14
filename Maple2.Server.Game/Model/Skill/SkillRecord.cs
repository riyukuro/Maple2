using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.Skill;

public class SkillRecord {
    public readonly SkillMetadata Metadata;
    public SkillMetadataMotion Motion => Metadata.Data.Motions[MotionPoint];
    public SkillMetadataAttack Attack => Motion.Attacks[AttackPoint];

    public int SkillId => Metadata.Id;
    public short Level => Metadata.Level;

    public readonly long CastUid;
    public long TargetUid;
    public readonly IActor Caster;

    public int ServerTick;
    public byte MotionPoint { get; private set; }
    public byte AttackPoint { get; private set; }

    public Vector3 Position;
    public Vector3 CastPosition; // the cast's position as taken at the use; attack packets are held to it
    public readonly SkillHitBudget Hits = new();
    public readonly VibrateRules.Budget Vibrates = new(); // the Vibrate packets taken, per attack point and object
    public Vector3 ImpactPosition;
    public Vector3 Direction; // Sometimes Rotation
    public Vector3 CastDirection; // the use's direction (CSkillRecord m_ptDir), zero when the cast is not moved by key
    public Vector3 Rotation;
    public float Rotate2Z; // the use's key-move override (CSkillRecord m_fCooldown); 0 takes the motion's own
    public bool Unknown;

    public bool IsHold;
    public int HoldInt;
    public string HoldString = string.Empty;
    public long ItemUid;
    public bool ItemConsumed; // the cast has taken its item
    public bool SplashPlaced; // the attack point's region skills are placed by the cast itself, not at the hit targets
    public long CancelTick; // the field tick of the cast's cancel, 0 while it is not cancelled

    public long StateNextTick; // Used for state skills only

    public ConcurrentDictionary<int, IActor> Targets;

    public SkillRecord(SkillMetadata metadata, long castUid, IActor caster) {
        Metadata = metadata;
        CastUid = castUid;
        Caster = caster;
        Targets = [];
    }

    public bool TrySetMotionPoint(byte motionPoint) {
        if (Metadata.Data.Motions.Length <= motionPoint) {
            return false;
        }

        MotionPoint = motionPoint;
        return true;
    }

    public bool TrySetAttackPoint(byte attackPoint) {
        if (Motion.Attacks.Length <= attackPoint) {
            return false;
        }

        AttackPoint = attackPoint;
        // Each attack point must start with a clean target set.
        // Without this, targets from a prior attack point (e.g. a Friendly AP that includes the caster)
        // bleed into subsequent attack points, causing incorrect damage and splash placement.
        Targets.Clear();
        return true;
    }

    public override string ToString() {
        return $"Uid:{CastUid}, SkillId:{SkillId}, Level:{Level}, MotionPoint:{MotionPoint}, AttackPoint:{AttackPoint}\n"
               + $"- Position:{Position}\n"
               + $"- Rotation:{Rotation}\n"
               + $"- Direction:{Direction}";
    }
}

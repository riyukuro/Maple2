using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

// What an NpcControl entry carries for an npc's state (client reader: CNpc::ApplySyncBlock).
public static class NpcControlRules {
    // Bit 1 of the flags byte is the battle flag (the reader hands it to SetEmotion). When it rises the client shows
    // the npc's battle talk line and refreshes its HP bar. KMS2 sends 2 while the npc is in battle or stunned and 0
    // otherwise.
    public const byte BattleFlag = 2;

    public static byte Flags(bool inBattle, bool stunned) => inBattle || stunned ? BattleFlag : (byte) 0;

    // A cast goes out as state 0 with the skill's sequence: the reader leaves the state alone for 0 and plays the
    // sequence. KMS2 never sends state 16 (the client's StateSkill) for an npc.
    public static ActorState WireState(ActorState state) => state == ActorState.PcSkill ? ActorState.None : state;

    // The hit state goes out once, with its fields; for the rest of the hold KMS2 sends state 0 with sequence -1.
    public static ActorState WireState(ActorState state, bool hitEntry) => state == ActorState.Hit && !hitEntry ? ActorState.None : WireState(state);

    // A stunned npc is sent with sequence -1.
    public static short WireSequence(ActorState state, short sequence) => state == ActorState.Stun ? (short) -1 : sequence;

    public static short WireSequence(ActorState state, short sequence, HitEntry? hit) => state == ActorState.Hit ? hit?.SequenceId ?? -1 : WireSequence(state, sequence);

    // The additional effect stun values that put a monster in the stun state. KMS2 sends the state for 1 and 3;
    // nothing shows it for the other values (2, 4..8, 99), so they are left out.
    public static bool Stuns(int stun) => stun is 1 or 3;

    // A held stun buff takes a monster only when its abnormalImmuneBreak is above the npc's abnormalImmune; an equal
    // value does not break it. The buff stays held either way. The client loads both values and reads neither, so the
    // gate is the server's. Chosen here like the hit state's hitImmuneBreak > hitImmune; players are not gated.
    public static bool StunTakes(int abnormalImmuneBreak, int abnormalImmune) => abnormalImmuneBreak > abnormalImmune;

    // The fields of a state 13 (hit) entry. Client: the reader fills StateHitData and StateHitNPC uses them: Delay is
    // the stay after the sequence, PushTime the time the push (and the lift arc) takes, Lift the arc's height, Down
    // plays the get-up sequence at the end. KMS2 sends the attack's hitPauseTime, pushduration and pushDown there;
    // the lift is 0 in every KMS2 packet, so pushUpDistance for it is chosen here.
    public readonly record struct HitEntry(short SequenceId, float Delay, float PushTime, float Lift, bool Down);

    // A damaging hit puts a monster in the hit state when the attack's hitImmuneBreak is above the npc's hitImmune.
    // KMS2 does it for npc classes 1 and 2 only (never for class 3 and up, whatever their hitImmune). A hit without
    // a push does it only to an npc that stands or walks: not during a cast, not while the last hit's hold runs. A
    // hit with a push does it in those too and ends the cast. Chosen here: none while spawning or stunned. Super
    // armor that holds (SuperArmorRules.Holds) keeps it off as well.
    public static bool HitReacts(int hitImmuneBreak, int hitImmune, int npcClass, bool push, ActorState state, bool armorHolds = false) {
        if (hitImmuneBreak <= hitImmune || npcClass >= 3 || armorHolds) {
            return false;
        }
        return state switch {
            ActorState.Spawn or ActorState.Regen or ActorState.Stun or ActorState.Dead => false,
            ActorState.PcSkill or ActorState.Hit => push,
            _ => true,
        };
    }

    // Where a hit came from: the damage record's origin (the caster, or the region for a skill placed on the
    // ground), the attacker's own position, the facing a type 5 push follows (PushFiveDirection) and the record's
    // facing a type 6 push reads (HitFacing).
    public readonly record struct HitSource(Vector3 Origin, Vector3 Attacker, Vector3 Forward, Vector3 Facing = default);

    // The record's facing (CDamageRecord m_ptHit). A cast's hit takes the cast's direction from the packet, which
    // carries it as whole numbers (ReadVector3Short); a region's hit never sets it, so it stays zero.
    public static Vector3 HitFacing(bool region, Vector3 castDirection) => region ? Vector3.Zero : Short(castDirection);

    // The record's origin (m_ptPos) types 1, 2 and 4 measure from. A cast's hit takes the position its packet carries
    // (the impact, as whole numbers); a region's hit takes the target's own position from its packet (the fire
    // point, cut to whole numbers: SkillDamagePacket.RegionPosition).
    public static Vector3 HitOrigin(bool region, Vector3 castPosition, Vector3 targetPosition) =>
        region ? SkillDamagePacket.RegionPosition(targetPosition) : Short(castPosition);

    // Where a hit on an npc came from, read from what its damage packet sends. A region names its own id as the
    // owner, a cast its caster's. The record's position and direction are the target's entry (DamageRecordTarget.For:
    // a region's fire point and the direction from there, a cast's caster position and push direction); the cast's
    // position and direction are the packet's impact and direction. A caster at the map origin (a trigger's) is no
    // attacker to measure from, so the record's position stands in.
    public static HitSource Source(int ownerId, int casterId, Vector3 castPosition, Vector3 castDirection, Vector3 recordPosition,
                                   Vector3 recordDirection, Vector3 casterPosition, Vector3 casterFront, int attackMethodType) {
        bool region = ownerId != casterId;
        Vector3 attacker = casterPosition.LengthSquared() < 0.001f ? recordPosition : casterPosition;
        Vector3 forward = region ? PushFiveDirection(true, attackMethodType, recordDirection, casterFront) : recordDirection;
        return new HitSource(HitOrigin(region, castPosition, recordPosition), attacker, forward, HitFacing(region, castDirection));
    }

    // A vector as the client reads it from three shorts (CInPacket::ReadVector3Short): no offset added back.
    private static Vector3 Short(Vector3 vector) {
        Vector3S wire = vector;
        return new Vector3(wire.X, wire.Y, wire.Z);
    }

    // The direction a type 5 push follows, which the hit names for its target. The client takes the damage record's
    // push direction and falls back to the attacker's facing only when it is zero (PCBase::vfunc_107). A region's
    // hit fills it from the target's direction in the packet (CSplashDamageRecord::Init): the direction from the
    // fire point to the target (DamageRecordTarget.For). A cast's hit fills it from the cast's direction only for an
    // attack whose hit comes from its arrow (attackMethodType 1, CSceneStateGame::FUN_14171cdf0) and leaves it zero
    // otherwise, so the caster's facing. The cast's direction is what the packet carries (whole numbers, HitFacing),
    // unitized.
    public static Vector3 PushFiveDirection(bool region, int attackMethodType, Vector3 damageDirection, Vector3 casterFront) {
        Vector3 direction = region ? damageDirection : HitFacing(false, damageDirection);
        if ((region || attackMethodType == 1) && direction.LengthSquared() > 0.001f) {
            return Vector3.Normalize(direction);
        }
        return casterFront;
    }

    // How far a push moves the npc, level with the ground (the server keeps it on the navmesh). Client
    // (PCBase::vfunc_107), by push type: 1 pushes pushdistance away from the origin; 2 pulls toward the origin by
    // pushdistance but no closer than 150 to the attacker (so one standing nearer is put back out to 150); 4 pulls
    // all the way to the origin; 5 pushes pushdistance along the attacker's facing; 6 pushes pushdistance along
    // -forward of the record's facing, which the client reads as angles (FUN_140a01250: forward is -Y turned by -z
    // degrees, (sin z, -cos z) with efd's row-major Z rotation), so (-sin z, cos z). 3, -1 and any other type leave
    // the direction zero, so the npc reacts as pushed but does not move.
    public static Vector3 PushOffset(int type, HitSource source, Vector3 target, float distance) {
        Vector2 direction;
        float length = distance;
        switch (type) {
            case 1:
                direction = new Vector2(target.X - source.Origin.X, target.Y - source.Origin.Y);
                break;
            case 2:
                direction = new Vector2(source.Origin.X - target.X, source.Origin.Y - target.Y);
                length = Math.Min(Vector3.Distance(source.Attacker, target) - 150, distance);
                break;
            case 4:
                direction = new Vector2(source.Origin.X - target.X, source.Origin.Y - target.Y);
                length = direction.Length();
                break;
            case 5:
                direction = new Vector2(source.Forward.X, source.Forward.Y);
                break;
            case 6:
                float angle = source.Facing.Z * MathF.PI / 180;
                direction = new Vector2(-MathF.Sin(angle), MathF.Cos(angle));
                break;
            default:
                return default;
        }
        if ((type != 4 && distance <= 0) || Math.Abs(length) < 0.001f || direction.LengthSquared() < 0.001f) {
            return default;
        }
        direction = Vector2.Normalize(direction) * length;
        return new Vector3(direction.X, direction.Y, 0);
    }

    // PushTime is 0 when the npc did not move (no push, or it stands against a wall or a ledge already).
    public static HitEntry Hit(short sequenceId, float hitDelay, SkillMetadataPush? push, bool moved) {
        return new HitEntry(sequenceId, hitDelay, moved && push != null ? push.Duration : 0, push?.UpDistance ?? 0, push is { Down: not 0 });
    }

    // How long the npc holds still after a hit, in ms. Client (StateHitNPC): the state ends at the later of the
    // sequence's end and the entry's pause plus push time; with the down byte the get-up sequence (Damg_C) follows.
    public static long HitHold(float sequenceTime, float aniSpeed, float pushTime, float delay, float getUpTime) {
        float speed = aniSpeed > 0 ? aniSpeed : 1;
        return (long) ((Math.Max(sequenceTime / speed, pushTime + delay) + getUpTime / speed) * 1000);
    }

    // Hits wait here between the thread that dealt the damage and the field thread, which applies them in the
    // npc's next update. Add is safe from any thread; Apply runs each waiting hit once, in order.
    public sealed class PendingHits {
        private readonly ConcurrentQueue<(HitSource Source, SkillMetadataAttack Attack)> hits = new();

        public void Add(HitSource source, SkillMetadataAttack attack) => hits.Enqueue((source, attack));

        public int Apply(Func<HitSource, SkillMetadataAttack, bool> apply) {
            int applied = 0;
            for (int count = hits.Count; count > 0 && hits.TryDequeue(out (HitSource Source, SkillMetadataAttack Attack) hit); count--) {
                if (apply(hit.Source, hit.Attack)) {
                    applied++;
                }
            }
            return applied;
        }
    }
}

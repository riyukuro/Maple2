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

    // Bit 0 of the flags byte snaps the npc to the entry's rotation. KMS2 and GMS2 set it on every jump entry with
    // the arc data (flags 3 in all 4 KMS2 and 715 GMS2 of them) and in a few other entries. The client reads it only
    // when no arc is moving, so on a jump it changes nothing seen; it is sent for the retail shape.
    public const byte RotateFlag = 1;

    public static byte JumpFlags(byte flags) => (byte) (flags | RotateFlag);

    // A cast goes out as state 0 with the skill's sequence: the reader leaves the state alone for 0 and plays the
    // sequence. KMS2 never sends state 16 (the client's StateSkill) for an npc.
    // An npc's emote goes out as state 1 with its sequence. KMS2 sends emotion sequences in state 1 (7,786 entries) and
    // never state 11 or 15 for an npc: the client's npc has neither state, so it drops the change (CStateManager::
    // ChangeState returns for a state the npc has not registered).
    public static ActorState WireState(ActorState state) => state switch {
        ActorState.PcSkill => ActorState.None,
        ActorState.Emotion or ActorState.EmotionIdle => ActorState.Idle,
        _ => state,
    };

    // The hit state goes out once, with its fields; for the rest of the hold KMS2 sends state 0 with sequence -1.
    // A walk in battle (a chase) goes out as state 0 with its sequence: KMS2 sends Run in state 0 with the battle flag
    // in 82,487 + 4,481 moving entries, and state 2 has the battle flag in 301 of 237,896 entries (GMS2: 248,051 Run
    // entries in state 0, 15,546 in state 2). The client keeps its state for 0 and moves the npc by the velocity.
    // The entry that ends a trigger npc's spawn state (spawnEnd) goes out as state 0 with sequence -1 (SpawnSequenceId)
    // on the spawn's serial: KMS2 sends it after 17 in 1,680 of 1,935 spawns, then the next state on a new serial within
    // 400 ms. The client only places the npc for an entry of the same serial, so its spawn state runs to its end.
    public static ActorState WireState(ActorState state, bool hitEntry, bool inBattle = false, bool spawnEnd = false) =>
        state == ActorState.Spawn && spawnEnd ? ActorState.None
        : state == ActorState.Hit && !hitEntry ? ActorState.None
        : state == ActorState.Walk && inBattle ? ActorState.None
        : WireState(state);

    // Whether an entry keeps the last entry's serial. The client enters the entry's state only on a new serial. KMS2
    // repeats the spawn and regen states with the serial they started with (all 13,011 repeated spawn entries and
    // 5,717 regen entries in KMS2), so the client's spawn and regen states run once.
    public static bool KeepsSerial(ActorState wireState, ActorState? lastWireState, bool spawnEnd = false) =>
        spawnEnd && wireState == ActorState.None && lastWireState == ActorState.Spawn
        || wireState is ActorState.Spawn or ActorState.Regen && wireState == lastWireState;

    // The sequence the client's spawn state plays for an event spawn point: its SpawnAnimation, Walk_A when that is
    // empty (WorldService::FUN_141bcfff0). It plays whatever spawn_monster's auto_target says: of the KMS2 trigger
    // spawns at points the trigger data gives one value, 813 of 1,600 with auto_target 0 and 332 of 491 with 1 carry the
    // sequence (the rest have models without it), and 653 and 71 go into battle within 2 s, by the map; GMS2: 2,607
    // of 5,361 and 1,330 of 1,578, battle 1,365 and 576. Npcs of 1 get the talk state too (KMS2 15, GMS2 26).
    public static string SpawnSequence(string spawnAnimation) => string.IsNullOrEmpty(spawnAnimation) ? "Walk_A" : spawnAnimation;

    // How long a trigger's npc stays in the spawn state, in ms. The client's StateSpawn hides the npc for 100 to 399 ms
    // (a draw from its object id), then plays the spawn point's sequence at speed 1 and goes to idle at its end; the
    // server holds the state for the longest delay and the sequence, so the idle that follows does not cut it.
    // A sequence the npc's model lacks has no length (the client idles right after its delay), so the hold is 400 ms;
    // KMS2 holds those spawns 200 to 500 ms and the Regen_A ones 1.3 to 1.8 s (10th to 90th percentile).
    public static long SpawnHold(float sequenceSeconds) => 400 + (long) (Math.Max(sequenceSeconds, 0) * 1000);

    // The sequence of a spawn state entry: -1 when the npc's model lacks the point's sequence (KMS2 sends -1 in
    // 931 spawns, all of models without Regen_A, and Regen_A in 894 spawns, all of models with it) and in
    // the entry that ends the state; otherwise the sequence playing.
    public static short SpawnSequenceId(bool modelHasSequence, bool spawnEnd, short playing) => modelHasSequence && !spawnEnd ? playing : (short) -1;

    // A stunned npc is sent with sequence -1.
    // Stun and a pet's revive state (StateSummonRevive picks its own motion) go out without a sequence.
    public static short WireSequence(ActorState state, short sequence) => state is ActorState.Stun or ActorState.SummonRevive ? (short) -1 : sequence;

    public static short WireSequence(ActorState state, short sequence, HitEntry? hit) => state == ActorState.Hit ? hit?.SequenceId ?? -1 : WireSequence(state, sequence);

    // The entry's speed (short x100; the client plays the entry's sequence at it, CNpc::ApplySyncBlock). Retail captures
    // send 1 in every entry outside the hit state: walk, run, idle, regen and attack entries alike, also for an npc whose
    // model anispeed is 1.1 or 1.5 and where the npc's skill motion has a sequenceSpeed of 1.5 or 1.3 (the client plays a
    // skill motion at its own speed, StateSkill::GetMotionSpeedScale, and a Run_ sequence at the model's anispeed,
    // CNpc::ApplySyncBlock). Only a hit entry and the hold after it (state 0, sequence -1) carry another speed (0.7 or
    // 0.8 in 2,059 of them), which is the hit's sequence speed here. The server's own clock keeps each sequence's speed.
    public static short WireAnimSpeed(float sequenceSpeed, bool hit) => hit ? (short) (sequenceSpeed * 100) : (short) 100;

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

    // A dot tick's hit: the effect's OffensiveProperty hitImmuneBreak (35 KR effects, e.g. 10300032 at 5), no push and
    // no hold after the sequence. The client gives a player's dot tick (CDotDamageRecord) the hitImmuneBreak of its
    // effect's level data (+0x298, the Offensive node's value; CDotDamageRecord::vfunc_1, skillrecord.cpp:650) and a
    // tick that took health puts the holder in the hit state when that value is above its hitImmune and it holds no
    // super armor (PC::vfunc_34, pc.cpp:980). An npc's hit state is the server's, so an npc's tick takes HitReacts
    // the same way (no push: only standing or walking), and its super armor keeps it off as the client's does.
    public static SkillMetadataAttack DotHit(int hitImmuneBreak) => DotHitAttack with { HitImmuneBreak = hitImmuneBreak };

    private static readonly SkillMetadataAttack DotHitAttack = new(Point: string.Empty, PointGroup: 0, TargetCount: 0, MagicPathId: 0,
        CubeMagicPathId: 0, HitImmuneBreak: 0, BrokenOffence: 0, CompulsionTypes: [], Pet: null, Range: null!, Arrow: null!,
        Damage: new SkillMetadataDamage(Count: 0, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: false, Value: 0,
            DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        Skills: [], SkillsOnDamage: []);

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
    // ground), the attacker's own position (none when the client finds no attacker object), the facing a type 5 push
    // follows (PushFiveDirection) and the record's facing a type 6 push reads (HitFacing).
    public readonly record struct HitSource(Vector3 Origin, Vector3? Attacker, Vector3 Forward, Vector3 Facing = default);

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
    // position and direction are the packet's impact and direction. A caster at the map origin (a trigger's field
    // actor) is an owner id the client finds no object for: no attacker, so a type 2 pull is not held at 150 and a type
    // 5 push with no direction of its own has no facing to fall back to (it does not move).
    public static HitSource Source(int ownerId, int casterId, Vector3 castPosition, Vector3 castDirection, Vector3 recordPosition,
                                   Vector3 recordDirection, Vector3 casterPosition, Vector3 casterFront, int attackMethodType) {
        bool region = ownerId != casterId;
        Vector3? attacker = casterPosition.LengthSquared() < 0.001f ? null : casterPosition;
        Vector3 front = attacker == null ? Vector3.Zero : casterFront;
        Vector3 forward = region ? PushFiveDirection(true, attackMethodType, recordDirection, front) : recordDirection;
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
    // pushdistance but no closer than 150 to the attacker (so one standing nearer is put back out to 150; with no
    // attacker object the full pushdistance); 4 pulls
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
                if (source.Attacker is { } attacker) {
                    length = Math.Min(Vector3.Distance(attacker, target) - 150, distance);
                }
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

    // How far below a ledge (map units) the floor may be for a push or a dash to step down onto it: the ground
    // height the client probes ahead each frame (CPushControl, hitcontrol.cpp; the linear and move motion
    // controllers, motioncontroller.cpp). A push with pushFall goes over any drop.
    public const float PUSH_DROP = 150;
    public const float DASH_DROP = 750;

    public static float PushDrop(SkillMetadataPush push) => push.Fall ? float.PositiveInfinity : PUSH_DROP;

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

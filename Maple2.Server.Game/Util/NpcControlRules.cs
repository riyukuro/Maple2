using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

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
    // ground), the attacker's own position and the attacker's facing.
    public readonly record struct HitSource(Vector3 Origin, Vector3 Attacker, Vector3 Forward);

    // How far a push moves the npc, level with the ground (the server keeps it on the navmesh). Client
    // (PCBase::vfunc_107), by push type: 1 pushes pushdistance away from the origin; 2 pulls toward the origin by
    // pushdistance but no closer than 150 to the attacker (so one standing nearer is put back out to 150); 4 pulls
    // all the way to the origin; 5 pushes pushdistance along the attacker's facing. 3, -1 and any other type leave
    // the direction zero, so the npc reacts as pushed but does not move; 6 (-forward of the record's facing) is not
    // in the KR data and is given no move here.
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

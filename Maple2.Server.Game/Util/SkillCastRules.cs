using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// Checks on a player's skill cast that the client makes itself and the server repeats before a cost is paid.
public static class SkillCastRules {
    // Chosen here: a use that comes up to this long before the cooldown's end is still taken, so a client whose
    // clock runs a little ahead is not refused. KMS2 takes a second use 4.0 s after the first on a 5 s cooldown.
    public const long CooldownSlackMs = 1500;

    public static bool RefusedDead(bool isDead, bool allowDead) => isDead && !allowDead;

    public static bool OnCooldown(long tick, long endTick) => tick < endTick - CooldownSlackMs;

    // The cooldown that refuses a use of the skill at this tick: its own, or that of a skill of the same cooldown
    // group (group 0 is no group). The one that ends last when several do.
    public static SkillCooldown? BlockingCooldown(IEnumerable<SkillCooldown> cooldowns, int skillId, int groupId, long tick) {
        SkillCooldown? blocking = null;
        foreach (SkillCooldown cooldown in cooldowns) {
            if (cooldown.SkillId != skillId && (groupId == 0 || cooldown.GroupId != groupId)) {
                continue;
            }
            if (OnCooldown(tick, cooldown.EndTick) && (blocking == null || cooldown.EndTick > blocking.EndTick)) {
                blocking = cooldown;
            }
        }
        return blocking;
    }

    // A cast takes its item once, however often its cost is asked for.
    public static bool ConsumesItem(bool useItem, bool itemConsumed) => useItem && !itemConsumed;

    // The target's resistance against an attack counted by this attack stat.
    public static BasicAttribute ResistanceOf(BasicAttribute attack) {
        return attack == BasicAttribute.MagicalAtk ? BasicAttribute.MagicalRes : BasicAttribute.PhysicalRes;
    }

    // A job skill's cooldown, counted from one accepted cast to the next. In KMS2 the casts of a skill with a
    // cooldown of 1 s or more never come sooner than the cooldown by the cast's own tick; shorter cooldowns are
    // cut by the attack speed, down to 0.8 of their length.
    // Chosen here: a cooldown of 1 s or more refuses a cast that comes more than this long before its end.
    public const long JobCooldownSlackMs = 500;
    // Chosen here: a cooldown under 1 s refuses a cast that comes sooner than this share of it.
    public const float ShortCooldownShare = 0.75f;
    // Chosen here: the time between two arrivals may be this much shorter than the time between the two casts.
    public const long ArrivalAllowanceMs = 500;

    // The last accepted cast of a skill: the cooldown it started and when, by the server's clock and by the
    // tick the client sent with the cast.
    public readonly record struct LastCast(int SkillId, int GroupId, long CooldownMs, long ServerTick, int CastTick, long AttackSpeed = 100, float SequenceSpeed = 1f, int MoveType = 0);

    // The shortest time after a cast at which the next one is taken. The client starts the cooldown of a skill whose
    // cooldown is not shown in the UI (beginCondition useCoolDownShowUI 0) at cooldown / the cast motion's speed
    // (MyPC::FUN_141525ff0 with StateSkill::GetMotionSpeedScale: the motion's own speed x the attack speed, the
    // motion's own speed alone for move type 3) and applies no cooldown reduction to it; a shown cooldown runs in
    // full. The flag is not ingested. Chosen here: a cooldown under 1 s stands in for it (in the KR data the skills
    // with a cooldown under 1 s have the flag 0 but for four with 0.4 s, which are only taken sooner by this), and
    // the cast is taken from ShortCooldownShare of that time on; an attack speed under 100 counts as 100.
    public static long RequiredGap(long cooldownMs, long attackSpeed = 100, float sequenceSpeed = 1f, int moveType = 0) {
        if (cooldownMs <= 0) {
            return 0;
        }
        return cooldownMs >= 1000 ? cooldownMs - JobCooldownSlackMs : (long) (cooldownMs * ShortCooldownShare / MotionSpeed(sequenceSpeed, attackSpeed, moveType));
    }

    // The time between two casts: the shorter of the client's own ticks and the arrivals plus the allowance, so
    // a client cannot lengthen it with its tick. A cast tick that runs backwards is not counted.
    public static long CastGap(int castTick, int lastCastTick, long serverTick, long lastServerTick, long allowanceMs = ArrivalAllowanceMs) {
        long arrival = serverTick - lastServerTick + allowanceMs;
        long client = unchecked(castTick - lastCastTick);
        return client < 0 ? arrival : Math.Min(client, arrival);
    }

    // Whether a cast of the skill comes too early after its own last cast or after the last cast of a skill of
    // the same cooldown group (group 0 is no group).
    // The higher of the attack speed at the last cast and now counts.
    public static bool CooldownRefuses(IEnumerable<LastCast> casts, int skillId, int groupId, int castTick, long serverTick, long attackSpeed = 100) {
        foreach (LastCast last in casts) {
            if (last.SkillId != skillId && (groupId == 0 || last.GroupId != groupId)) {
                continue;
            }
            if (CastGap(castTick, last.CastTick, serverTick, last.ServerTick) < RequiredGap(last.CooldownMs, Math.Max(last.AttackSpeed, attackSpeed), last.SequenceSpeed, last.MoveType)) {
                return true;
            }
        }
        return false;
    }

    // The time a cast's motion takes before the next cast. The client takes the next skill from the motion's
    // "inputcombo" key on (from "selfinputcombo" for the same skill again, when the motion has one); a motion
    // without it is counted to its "combo" key, else to its "end" key. The key times are in seconds at speed 1.
    // The client plays the motion at a speed its Lua (calc_pc_asiR) makes of the motion's own speed and the attack
    // speed stat. The Lua body is not known: speed = motion speed x attack speed / 100 is fitted here over KMS2
    // casts at attack speed 100 to 121. A motion of move type 3 is played at its own speed whatever the attack speed
    // (StateSkill::GetMotionSpeedScale).
    // A motion with a loop (a held skill) leaves the loop when the hold ends, so a key after "loopend" can come up
    // to one loop sooner: such a key is counted without the loop's length.
    // Chosen here: a cast is taken up to this long before that time.
    public const long MotionSlackMs = 100;
    // Chosen here: the time between two arrivals may be this much shorter than the time between the two casts. It
    // is longer than for cooldowns because this check is on every skill, and two casts held up together arrive at once.
    public const long MotionArrivalAllowanceMs = 1000;

    // The rotation a cast carries: the one given with it, else (an npc's cast gives none) the caster's own. The client
    // takes the cast's facing from its z (CSceneStateGame::OnSkillUse).
    public static Vector3 CastRotation(in Vector3 given, in Vector3 caster) => given == default ? caster : given;

    // The last accepted cast that plays a motion: the times of its keys, its speed and when it came.
    public readonly record struct LastMotion(int SkillId, float NextKey, float SelfKey, float SequenceSpeed, long AttackSpeed, long ServerTick, int CastTick, int MoveType = 0);

    // The earliest time of a key in a motion's sequence, null when the sequence lacks it.
    public static float? KeyTime(IEnumerable<AnimationKey> keys, string name) {
        float? time = null;
        foreach (AnimationKey key in keys) {
            if (string.Equals(key.Name, name, StringComparison.OrdinalIgnoreCase) && (time == null || key.Time < time)) {
                time = key.Time;
            }
        }
        return time;
    }

    // The key times after which another skill and the same skill are taken; null for a sequence with none.
    public static (float Next, float Self)? MotionKeys(IReadOnlyCollection<AnimationKey> keys) {
        float? next = KeyTime(keys, "inputcombo") ?? KeyTime(keys, "combo") ?? KeyTime(keys, "end");
        if (next == null) {
            return null;
        }
        float? loopStart = KeyTime(keys, "loopstart");
        float? loopEnd = KeyTime(keys, "loopend");
        float self = KeyTime(keys, "selfinputcombo") ?? next.Value;
        if (loopStart == null || loopEnd == null || loopEnd <= loopStart) {
            return (next.Value, self);
        }
        float loop = loopEnd.Value - loopStart.Value;
        return (next > loopEnd ? next.Value - loop : next.Value, self > loopEnd ? self - loop : self);
    }

    // A cancelled cast takes no more attack packets once this time has passed since its cancel. KMS2 still answers
    // an attack that comes up to 0.87 s after the cancel of its cast with damage (the latest of 23 such casts, target
    // packets only); the 2 s is chosen here, well above it, since the time is taken between two arrivals.
    public const long CancelledAttackWindowMs = 2000;

    public static bool CancelledRefuses(long cancelTick, long tick) => cancelTick != 0 && tick - cancelTick > CancelledAttackWindowMs;

    // The fitted speed of a motion. An attack speed under 100 is counted as 100 (chosen here: no KMS2 cast has one).
    public const int UnscaledMoveType = 3;

    public static float MotionSpeed(float sequenceSpeed, long attackSpeed, int moveType = 0) {
        float speed = sequenceSpeed > 0 ? sequenceSpeed : 1f;
        return moveType == UnscaledMoveType ? speed : speed * MathF.Max(1f, attackSpeed / 100f);
    }

    public static long MotionRequiredGap(float keySeconds, float speed) {
        return (long) MathF.Round(keySeconds / speed * 1000) - MotionSlackMs;
    }

    // Whether a cast comes before the last cast's motion lets the next one in. A skill that is active at once
    // (ImmediateActive) is taken at any time, as in KMS2. The higher of the attack speed at the last cast and now
    // counts.
    public static bool MotionRefuses(LastMotion last, int skillId, bool immediateActive, long attackSpeed, int castTick, long serverTick) {
        if (immediateActive) {
            return false;
        }
        float key = last.SkillId == skillId ? last.SelfKey : last.NextKey;
        float speed = MotionSpeed(last.SequenceSpeed, Math.Max(last.AttackSpeed, attackSpeed), last.MoveType);
        return CastGap(castTick, last.CastTick, serverTick, last.ServerTick, MotionArrivalAllowanceMs) < MotionRequiredGap(key, speed);
    }

    // The skills whose cooldown is checked as a job skill's: those of a job's tree and the job skill ids.
    public static bool IsJobSkill(int skillId, bool inAnyTree) => inAnyTree || skillId is >= 10000000 and <= 11999999;

    // A tick sync of a running cast pays the cast's spirit and stamina again only for a skill that continues
    // while held, as KMS2 does; no other skill and no other cost is paid twice.
    public static bool SyncPays(bool continueSkill) => continueSkill;

    // The health a cast costs: the skill's rate (negative in the data) of the current health.
    public static int HpCost(long currentHp, float hpRate) => (int) (currentHp * Math.Abs(hpRate));

    // Chosen here: a cast whose health cost would leave no health is refused.
    public static bool HpCostRefused(long currentHp, int cost) => cost > 0 && cost >= currentHp;

    // Every skill of every job's tree, main and sub.
    public static HashSet<int> TreeSkills(JobTable jobTable) {
        var skills = new HashSet<int>();
        foreach (JobTable.Entry entry in jobTable.Entries.Values) {
            foreach (JobTable.Skill[] jobSkills in entry.Skills.Values) {
                foreach (JobTable.Skill skill in jobSkills) {
                    skills.Add(skill.Main);
                    skills.UnionWith(skill.Sub);
                }
            }
        }
        return skills;
    }

    // One job's tree: each main skill to itself and each sub skill to its main skill. The awakening rank counts
    // only for an awakened job.
    public static Dictionary<int, int> MainSkills(JobTable.Entry entry, bool awakening) {
        var mainOf = new Dictionary<int, int>();
        foreach ((SkillRank rank, JobTable.Skill[] jobSkills) in entry.Skills) {
            if (rank == SkillRank.Awakening && !awakening) {
                continue;
            }
            foreach (JobTable.Skill skill in jobSkills) {
                foreach (int sub in skill.Sub) {
                    mainOf.TryAdd(sub, skill.Main);
                }
            }
        }
        foreach ((SkillRank rank, JobTable.Skill[] jobSkills) in entry.Skills) {
            if (rank == SkillRank.Awakening && !awakening) {
                continue;
            }
            foreach (JobTable.Skill skill in jobSkills) {
                mainOf[skill.Main] = skill.Main;
            }
        }
        return mainOf;
    }

    // A cast of a skill that is in a job's skill tree is refused when the caster's own tree lacks it or holds
    // it at a lower level. A skill in no tree (mount, lift-up, a skill a buff gives or changes to) is not checked.
    // learnedLevel: the level in the caster's tree (a sub skill's is its main skill's), null when not in it.
    public static bool TreeRefuses(bool inAnyTree, short? learnedLevel, short castLevel) {
        if (!inAnyTree) {
            return false;
        }
        return learnedLevel == null || learnedLevel <= 0 || castLevel > learnedLevel;
    }
}

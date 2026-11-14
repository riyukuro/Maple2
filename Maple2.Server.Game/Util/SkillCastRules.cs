using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Util;

// Checks on a player's skill cast that the client makes itself and the server repeats before a cost is paid.
public static class SkillCastRules {
    // Chosen here: a use that comes up to this long before the cooldown's end is still taken, so a client whose
    // clock runs a little ahead is not refused. KMS2 takes a second use 4.0 s after the first on a 5 s cooldown.
    public const long CooldownSlackMs = 1500;

    // linkSkillID (skillactionadditional.h:19, parsed at skillleveldata.cpp:346) names the skill whose learned
    // level an effect's skill takes: buff 10400291 heals half of Healing Prayer 10400071 per level, and splash
    // 10400187's rates follow 10400281's tooltip per level. Chosen here: with no link, or a caster that has not
    // learned the linked skill (an npc, another job), the effect keeps the level the data names.
    public static short LinkedLevel(short dataLevel, int linkId, Func<int, short?> learnedLevel) {
        if (linkId == 0) {
            return dataLevel;
        }
        short? level = learnedLevel(linkId);
        return level is > 0 ? level.Value : dataLevel;
    }

    // The level an effect's skill takes for this caster. Most effect skills have no link: they return before any
    // lookup.
    public static short LinkedLevel(IActor caster, SkillEffectMetadata.Skill skill) {
        if (skill.LinkId == 0) {
            return skill.Level;
        }
        short? learned = caster is FieldPlayer player ? player.Session.Config.Skill.SkillInfo.GetMainSkill(skill.LinkId)?.Level : null;
        return learned is > 0 ? learned.Value : skill.Level;
    }

    public static bool RefusedDead(bool isDead, bool allowDead) => isDead && !allowDead;

    // A state skill (a move state held on its key) takes its cost again every second while it is held. The
    // client keeps no count of its own: StateMoveSkill checks once a second from the state's start that the stamina left
    // covers the level's ep and leaves the state when it does not (statemanager.cpp:784-794, Unk_IntervalTimer armed on
    // entry), and it never takes the ep itself. The motion's sequence speed has no part in it.
    public const int StateSkillTickMs = 1000;

    public static long NextStateTick(long tick) => tick + StateSkillTickMs;

    public static bool OnCooldown(long tick, long endTick) => tick < endTick - CooldownSlackMs;

    // clearCooldownFromPVPZone, loaded with a skill's stateAttr (skilloverlapcompare.cpp:867) and an effect's
    // BasicProperty (additionalactionadditional.cpp:1073) and read nowhere else in the client: entering a PvP
    // zone (a map of type Pvp, as for clearEffectFromPVPZone) ends the cooldown of each flagged skill or effect.
    // The ids whose cooldown ends on entering a map of this type.
    public static List<int> PvpZoneClears(MapType mapType, IEnumerable<int> ids, Func<int, bool> flagged) {
        return mapType == MapType.Pvp ? ids.Where(flagged).ToList() : [];
    }

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

    // The share cut from a cooldown of this cooldown group by the buffs held: the sum of their reductions for the
    // group, one per buff (CSkillSystem::FUN_1417664e0 sums the held buffs' group reductions; group 0 is no group).
    public static float GroupReduceRate(IEnumerable<CooldownGroupRate[]?> held, int groupId) {
        float rate = 0;
        if (groupId == 0) {
            return rate;
        }
        foreach (CooldownGroupRate[]? rates in held) {
            if (rates == null) {
                continue;
            }
            foreach (CooldownGroupRate entry in rates) {
                if (entry.GroupId == groupId) {
                    rate += entry.Rate;
                }
            }
        }
        return rate;
    }

    // The cooldown the client starts for a skill, in ms (CSkillSystem::FUN_1417664e0): the skill's cooldown less
    // its share for the cooldown group's reduction and, unless the skill ignores cooldown reduction, less the
    // flat seconds and the share for the rates of the held buffs' cooldown invoke effects on the skill (the
    // client's skill options 1002 and 1001; the server's data names both invoke type 1, values and rates) and
    // the caster's cooldown reduction rate; never under 0.
    public static long CooldownMs(float cooldownSeconds, bool ignoreReduce, float groupRate, double reduceRate,
            int invokeSeconds = 0, float invokeRate = 0) {
        double rate = groupRate + (ignoreReduce ? 0 : reduceRate + invokeRate);
        double flat = ignoreReduce ? 0 : invokeSeconds;
        return (long) Math.Max(0, (cooldownSeconds - flat - cooldownSeconds * rate) * 1000);
    }

    // An effect's <RemoveRechargingCount> start on the record (SkillPacket.RechargeUses): settled to this tick, then
    // count uses back, never under none out. At none out the client clears the skill's cooldown, so the caller drops
    // the record.
    public static (long EndTick, int Uses) GiveBackRecharge(long endTick, int uses, long lengthMs, long tick, int count) {
        (endTick, uses) = SettleRecharge(endTick, uses, lengthMs, tick);
        return (endTick, Math.Max(uses - Math.Max(count, 0), 0));
    }

    // The recharge uses a cooldown record stands at by this tick, as the client counts them (CSkillSystem::Update
    // with FUN_1417687e0): each ended cooldown gives one use back, and starts again from its end while uses are
    // still out. Returns the end tick and uses left out.
    public static (long EndTick, int Uses) SettleRecharge(long endTick, int uses, long lengthMs, long tick) {
        while (uses > 0 && tick >= endTick) {
            uses--;
            if (uses > 0) {
                if (lengthMs <= 0) {
                    return (endTick, 0);
                }
                endTick += lengthMs;
            }
        }
        return (endTick, uses);
    }

    // A recharge chain's length when the cooldown the client would start now (CooldownMs) is shorter than the one
    // kept: the client starts each recharge cooldown again at the reductions held at that moment (FUN_1417664e0
    // from FUN_1417687e0), and the server, which settles the chain only when it looks at it, does not know when a
    // reduction came. Chosen here: the shorter length counts from then on and for the step that runs (its end moves
    // closer by the difference), so a reduction gained mid-chain never gets a cast refused; a longer one (a
    // reduction lost) leaves the kept length. Returns the end tick and length to keep.
    public static (long EndTick, long LengthMs) ShortenRecharge(long endTick, long keptMs, long nowMs) {
        if (nowMs >= keptMs || nowMs < 0) {
            return (endTick, keptMs);
        }
        return (endTick - (keptMs - nowMs), nowMs);
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

    // The last accepted cast of a skill: the cooldown it started and when, by the server's clock and by the
    // tick the client sent with the cast.
    public readonly record struct LastCast(int SkillId, int GroupId, long CooldownMs, long ServerTick, int CastTick, long AttackSpeed = 100, float SequenceSpeed = 1f, int MoveType = 0);

    // The shortest time after a cast at which the next one is taken. The client starts the cooldown of a skill whose
    // cooldown is not shown in the UI (beginCondition useCoolDownShowUI 0) at cooldown / the cast motion's speed
    // (MyPC::FUN_141525ff0 with StateSkill::GetMotionSpeedScale: the motion's own speed x the attack speed, the
    // motion's own speed alone for move type 3) and applies no cooldown reduction to it; a shown cooldown runs in
    // full. The flag is not ingested. Chosen here: a cooldown under 1 s stands in for it (in the KR data the skills
    // with a cooldown under 1 s have the flag 0 but for four with 0.4 s, which are only taken sooner by this), and
    // the cast is taken from ShortCooldownShare of that time on, at the motion speed of MotionSpeed.
    public static long RequiredGap(long cooldownMs, long attackSpeed = 100, float sequenceSpeed = 1f, int moveType = 0) {
        if (cooldownMs <= 0) {
            return 0;
        }
        return cooldownMs >= 1000 ? cooldownMs - JobCooldownSlackMs : (long) (cooldownMs * ShortCooldownShare / MotionSpeed(sequenceSpeed, attackSpeed, moveType));
    }

    // A cast that comes too early by the arrivals, with a tick further back than a held-up time sync explains.
    public const long ClientRefused = long.MaxValue;

    // The time a cast comes too early by. The time between the two arrivals at the server counts: a client cannot
    // lengthen it with its ticks. 0 when that time is long enough, whatever the ticks say: the client sets its
    // tick anew at every time sync (CMainSystem::SetServerTickBase, to the server's number as it arrives), so a
    // sync held up by the link or by a stalled client moves the ticks after it back by that long.
    // When the arrivals are closer (packets held up and delivered together), the time missing between them is
    // returned, to be drawn from the session's LagBudget. The client's own ticks may then lie closer than the time
    // needed, or run backwards, by up to ClockRate.CastBackMs: a stall that holds the casts holds the time sync
    // answer too, and the ticks after it lie lower by the time it was held. Further back than that is
    // ClientRefused and draws nothing. (ClockRate counts a tick that jumps back far as clock trouble.)
    public static long ArrivalShortfall(int castTick, int lastCastTick, long serverTick, long lastServerTick, long requiredMs) {
        long shortfall = requiredMs - (serverTick - lastServerTick);
        if (shortfall <= 0) {
            return 0;
        }
        long client = unchecked(castTick - lastCastTick);
        return client < requiredMs - ClockRate.CastBackMs ? ClientRefused : shortfall;
    }

    // The time a cast of the skill comes too early by (ArrivalShortfall) after its own last cast or after the last
    // cast of a skill of the same cooldown group (group 0 is no group): the longest when several count.
    // The higher of the attack speed at the last cast and now counts.
    public static long CooldownShortfall(IEnumerable<LastCast> casts, int skillId, int groupId, int castTick, long serverTick, long attackSpeed = 100) {
        long shortfall = 0;
        foreach (LastCast last in casts) {
            if (last.SkillId != skillId && (groupId == 0 || last.GroupId != groupId)) {
                continue;
            }
            long required = RequiredGap(last.CooldownMs, Math.Max(last.AttackSpeed, attackSpeed), last.SequenceSpeed, last.MoveType);
            shortfall = Math.Max(shortfall, ArrivalShortfall(castTick, last.CastTick, serverTick, last.ServerTick, required));
        }
        return shortfall;
    }

    // Whether the cast comes too early for a session with nothing left in its LagBudget.
    public static bool CooldownRefuses(IEnumerable<LastCast> casts, int skillId, int groupId, int castTick, long serverTick, long attackSpeed = 100) {
        return CooldownShortfall(casts, skillId, groupId, castTick, serverTick, attackSpeed) > 0;
    }

    // The time a cast's motion takes before the next cast. The client takes the next skill from the motion's
    // "inputcombo" key on (from "selfinputcombo" for the same skill again, when the motion has one); a motion
    // without it is counted to its "combo" key, else to its "end" key. The key times are in seconds at speed 1.
    // The client plays the motion at a speed its Lua (calc_pc_asiR) makes of the motion's own speed and the attack
    // speed stat (PCBase stat 0xd): speed = motion speed x clip(attack speed / 100, 0.1, 1.5), as the KR pack's
    // calc_pc_asiR has it. A motion of move type 3 is played at its own speed whatever the attack speed
    // (StateSkill::GetMotionSpeedScale).
    // A motion with a loop (a held skill) leaves the loop when the hold ends, so a key after "loopend" can come up
    // to one loop sooner: such a key is counted without the loop's length.
    // Chosen here: a cast is taken up to this long before that time.
    public const long MotionSlackMs = 100;

    // The rotation a cast carries: the one given with it, else (an npc's cast gives none) the caster's own. The client
    // takes the cast's facing from its z (CSceneStateGame::OnSkillUse).
    public static Vector3 CastRotation(in Vector3 given, in Vector3 caster) => given == default ? caster : given;

    // How an npc's cast of a motion starts. A level whose motions name no sequence (SkillLevelData::HasSequenceName,
    // false for a level with no motion too) is cast without the skill state: the client sends the use and fires the
    // first attack in the same call (MyPC::vfunc_102 -> StateSkill::FireFirstAttack), with no animation and no key to
    // wait for. Such a cast fires its attack 0 at once and ends. A level with a sequence plays the motion's and fires
    // at its keys; a motion whose sequence the model lacks (or names none in a level that has one) cannot play.
    public enum CastStart { Play, FireNow, Cancel }

    public static bool CastsWithoutSequence(IEnumerable<SkillMetadataMotion> motions) =>
        motions.All(motion => string.IsNullOrEmpty(motion.MotionProperty.SequenceName));

    public static CastStart StartOf(IEnumerable<SkillMetadataMotion> motions, Func<bool> playSequence) =>
        CastsWithoutSequence(motions) ? CastStart.FireNow : playSequence() ? CastStart.Play : CastStart.Cancel;

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

    // Whether the caster's own client gets the SkillCancel of a cast. KMS2 answers none of the cancels a client
    // sends (Skill 4) to that client; the players around get it. The cancel of a state skill, which the server
    // ends itself, goes to everyone, its owner too.
    public static bool CancelGoesToCaster(bool endedByServer) => endedByServer;

    public static bool CancelledRefuses(long cancelTick, long tick) => cancelTick != 0 && tick - cancelTick > CancelledAttackWindowMs;

    // The speed of a motion (calc_pc_asiR): the attack speed scales it between 0.1 and 1.5 times.
    public const int UnscaledMoveType = 3;

    public static float MotionSpeed(float sequenceSpeed, long attackSpeed, int moveType = 0) {
        float speed = sequenceSpeed > 0 ? sequenceSpeed : 1f;
        return moveType == UnscaledMoveType ? speed : speed * Math.Clamp(attackSpeed / 100f, 0.1f, 1.5f);
    }

    public static long MotionRequiredGap(float keySeconds, float speed) {
        return (long) MathF.Round(keySeconds / speed * 1000) - MotionSlackMs;
    }

    // The time a cast comes before the last cast's motion lets the next one in (ArrivalShortfall). A skill that is
    // active at once (ImmediateActive) is taken at any time, as in KMS2. The higher of the attack speed at the last
    // cast and now counts.
    public static long MotionShortfall(LastMotion last, int skillId, bool immediateActive, long attackSpeed, int castTick, long serverTick) {
        if (immediateActive) {
            return 0;
        }
        float key = last.SkillId == skillId ? last.SelfKey : last.NextKey;
        float speed = MotionSpeed(last.SequenceSpeed, Math.Max(last.AttackSpeed, attackSpeed), last.MoveType);
        return ArrivalShortfall(castTick, last.CastTick, serverTick, last.ServerTick, MotionRequiredGap(key, speed));
    }

    // Whether the cast comes too early for a session with nothing left in its LagBudget.
    public static bool MotionRefuses(LastMotion last, int skillId, bool immediateActive, long attackSpeed, int castTick, long serverTick) {
        return MotionShortfall(last, skillId, immediateActive, attackSpeed, castTick, serverTick) > 0;
    }

    // What the timing of a cast comes to. needMs: the longer of its motion and cooldown shortfalls. A cast on time
    // is taken. One that is early by the client's own ticks is refused. One that only arrives early is taken while
    // the session's LagBudget holds the time it is early by, unless the client's clock runs fast (ClockRate): its
    // ticks say nothing then, and the arrivals alone count.
    public static string? TimingRefusal(long needMs, bool clockFast, LagBudget lag, long now) {
        if (needMs <= 0) {
            return null;
        }
        if (needMs == ClientRefused) {
            return "too early, with a tick too far back";
        }
        if (clockFast) {
            return $"arrives {needMs} ms early and the client's clock runs fast";
        }
        return lag.TryDraw(needMs, now) ? null : $"arrives {needMs} ms early, {lag.Level(now)} ms of lag allowance left";
    }

    // The skills whose cooldown is checked as a job skill's: those of a job's tree and the job skill ids.
    public static bool IsJobSkill(int skillId, bool inAnyTree) => inAnyTree || skillId is >= 10000000 and <= 11999999;

    // A tick sync of a running cast pays the cast's spirit and stamina again only for a skill that continues
    // while held, as KMS2 does; no other skill and no other cost is paid twice.
    public static bool SyncPays(bool continueSkill) => continueSkill;

    // The spirit a cast costs, as the client works it out before it sends the cast (CSkillSystem, 0x14176b960):
    // the skill's cost less the flat value of the cost invokes, less the cost times (the invokes' rate minus the
    // summed consume spirit rate of the buffs held), in float, cut to a whole number, never below 0.
    public static int SpiritCost(long cost, int invokeValue, float invokeRate, float buffRate) {
        float rate = -1.0f * buffRate + invokeRate;
        long result = (long) ((float) (cost - invokeValue) - (float) cost * rate);
        return (int) Math.Clamp(result, 0, int.MaxValue);
    }

    // A skill with hand weapons in its condition (skill basic condition weapon lh rh) is cast only while one of
    // those rows fits the weapons held: each hand of the row is 0 (any) or the subtype held in that hand, and
    // the client sends no cast otherwise (CSkillSystem cast check 0x14176a8c0, answer 0x1c). A left hand that
    // holds the right hand's item (a two-handed weapon) counts as empty.
    public static bool WeaponRefuses(IReadOnlyCollection<(int LeftHand, int RightHand)> weapons, int heldLeft, int heldRight) {
        return weapons.Count > 0 && !weapons.Any(weapon =>
            (weapon.RightHand == 0 || weapon.RightHand == heldRight) && (weapon.LeftHand == 0 || weapon.LeftHand == heldLeft));
    }

    // The same check on the weapon action the client holds (PC::UpdateWeaponAction, pc.cpp:2839): each hand is
    // the subtype of the item worn there, 0 when the character cannot use that item (PC::CanUseItem) or the left
    // hand holds the right hand's item; the left hand is dropped when the weapon action table does not allow it
    // with the right hand; and the action is the table's row of exactly those hands. With no such row the client
    // holds no action, and a skill with weapon rows is not cast at all (cast check 0x14176a8c0, answer 0x1c).
    public static bool WeaponRefuses(IReadOnlyCollection<(int LeftHand, int RightHand)> weapons, WeaponActionTable.Action? held) {
        if (weapons.Count == 0) {
            return false;
        }
        return held == null || WeaponRefuses(weapons, held.LeftHand, held.RightHand);
    }

    // The subtypes PC::UpdateWeaponAction gives the two hands before the table is asked: an item the character
    // cannot use counts as none, and a left hand that holds the right hand's item (a two-handed weapon) as empty.
    public static (int Right, int Left) HandTypes(int rightType, long rightUid, bool rightUsable, int leftType, long leftUid, bool leftUsable) {
        int right = rightUsable ? rightType : 0;
        int left = !leftUsable || (leftUid != 0 && leftUid == rightUid) ? 0 : leftType;
        return (right, left);
    }

    // A skill with a spirit threshold in its condition (skill basic condition sp) is cast only with at least that
    // much spirit, the threshold being cut by the spirit cost reductions as the cost is (SpiritCost); the
    // client sends no cast otherwise (the same check, answer 0xb).
    public static bool SpiritThresholdRefuses(long reducedThreshold, long currentSpirit) => reducedThreshold > 0 && reducedThreshold > currentSpirit;

    // A held, enabled buff that lists the skill among the ones it disables stops the cast: the client does not
    // send it (CSkillSystem cast check, answer 0x1f).
    public static bool DisabledByBuff(IEnumerable<int[]?> heldLists, int skillId) {
        foreach (int[]? list in heldLists) {
            if (list != null && Array.IndexOf(list, skillId) >= 0) {
                return true;
            }
        }
        return false;
    }

    // A skill that names fields is cast only in one of them: the client does not send it elsewhere (cast check
    // answer 0x17). A code of 0 names no field.
    // The client does not cast a skill that takes its caster off the ride (kinds unrideOnUse) while it rides: it
    // asks to get off and casts once it is off (MyPC::FUN_141525580). A skill of one of the client's unride main
    // types counts as one whatever its unrideOnUse says (SkillData::IsUnrideOnUse). Chosen here: only on the
    // caster's own item ride; a passenger and a ride a buff gave are not refused.
    public static bool RideRefuses(bool unrideOnUse, int mainType, bool onOwnItemRide) {
        return (unrideOnUse || PlayedClientSkills.UnrideMainType(mainType)) && onOwnItemRide;
    }

    // The client does not cast a recharge skill while its cooldown runs and either no use is left or the skill's
    // defaultRechargingCooldownTime has not passed since its last use (MyPC::vfunc_102 with
    // CSkillSystem::CanUseRechargeSkill). The time a cast comes too early by, by the server's count of the uses:
    // usesOut and endTick are the settled record (SettleRecharge), 0 uses out is no cooldown.
    // Chosen here: a cast is taken up to JobCooldownSlackMs before a use comes back.
    public static long RechargeEmptyShortfall(int usesOut, int maxCount, long endTick, long tick) {
        if (maxCount <= 0 || usesOut < maxCount) {
            return 0;
        }
        return Math.Max(0, endTick - JobCooldownSlackMs - tick);
    }

    // The time a recharge skill's cast comes before its gap after the last use is over (ArrivalShortfall).
    // Chosen here: the cast is taken from ShortCooldownShare of the gap on, as for a short cooldown.
    public static long RechargeGapShortfall(float gapSeconds, int castTick, int lastCastTick, long serverTick, long lastServerTick) {
        if (gapSeconds <= 0) {
            return 0;
        }
        return ArrivalShortfall(castTick, lastCastTick, serverTick, lastServerTick, (long) (gapSeconds * 1000 * ShortCooldownShare));
    }

    // The main types the field's skillUseDisable lists, as the client keeps them: bit n for main type n
    // (CFieldData load, fielddata.cpp:708). A value outside 0..31 sets no bit here (the client's shift is undefined there).
    public static int FieldTypeMask(int[]? mainTypes) {
        int mask = 0;
        foreach (int type in mainTypes ?? []) {
            if (type is >= 0 and < 32) {
                mask |= 1 << type;
            }
        }
        return mask;
    }

    // The client refuses a cast whose main type has its bit in the enabled buffs' skillUseDisable masks or the
    // field's (PCBase::GetSkillUseDisableMask, CSkillSystem cast check answer 0x1e).
    // Only a cast's first motion is checked: the client checks the type when a cast starts and sends the next motion
    // of the same cast (motion point 1 and on) without it (CStateSkill, stateskill.cpp:2977), so a mask that lands
    // between motions does not stop the cast the client goes on playing. laterMotion: see LaterMotion.
    public static bool TypeDisabled(int buffMask, int fieldMask, int mainType, bool laterMotion = false) {
        if (laterMotion) {
            return false;
        }
        int mask = buffMask | fieldMask;
        return mask != 0 && mainType is >= 0 and < 32 && (mask & (1 << mainType)) != 0;
    }

    // A Use is the next motion of a cast only when the server took an earlier motion of it: the client sends the
    // next motion with the cast's own id, skill and the motion point one on (StateSkill::FUN_1417ee120,
    // stateskill.cpp:2971). A Use with a motion point past 0 and no such cast held is a cast start and is checked as one.
    public static bool LaterMotion(int motionPoint, int skillId, int? heldSkillId, int heldMotionPoint) {
        return motionPoint != 0 && heldSkillId == skillId && heldMotionPoint < motionPoint;
    }

    public static bool MapRefuses(int[] maps, int mapId) {
        return maps.Any(code => code != 0) && Array.IndexOf(maps, mapId) < 0;
    }

    // The item a cast uses up must be there before any other cost is paid: named, and in the pet's bag or the
    // caster's own.
    public static bool ItemMissing(bool consumesItem, long itemUid, bool inPetBag, bool inInventory) {
        return consumesItem && (itemUid == 0 || (!inPetBag && !inInventory));
    }

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

    // A cast of a skill in no job tree is refused when no client path gives the skill to a player (the ingest's
    // NoPlayerSource, PlayerSkillSources): an NPC or boss skill, or one only the server casts from a condition or an effect.
    public static bool SourceRefuses(bool inAnyTree, bool noPlayerSource) => !inAnyTree && noPlayerSource;

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

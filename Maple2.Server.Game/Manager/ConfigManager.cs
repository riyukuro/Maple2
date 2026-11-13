using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Manager;

public class ConfigManager {
    private const int TOTAL_HOT_BARS = 3;

    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private readonly IDictionary<int, KeyBind> keyBinds;
    private short activeHotBar;
    private readonly List<HotBar> hotBars;
    private IList<SkillMacro> skillMacros;
    private IList<Wardrobe> wardrobes;
    private IList<int> favoriteStickers;
    private readonly IList<long> favoriteDesigners;
    private readonly IDictionary<LapenshardSlot, int> lapenshards;
    private readonly IDictionary<int, SkillCooldown> skillCooldowns;
    // skillCooldowns and lastCasts are written from the session (casts, map loads, the save) and from the field
    // thread (an effect's cooldown reset or recharge give-back, BuffManager): each use locks skillCooldowns. A
    // SkillCooldown handed out (GetSkillCooldown, BlockingCooldown) is read after the lock.
    // The town cooldowns a PvP-zone entry ended, put back on the way out.
    public readonly PvpCooldownHold<SkillCooldown> PvpHold = NewPvpHold();
    private readonly IDictionary<int, SkillCastRules.LastCast> lastCasts;
    // The last town casts of those skills, put back with them: a job skill's cooldown is checked on its last cast
    // (CastTimingRefusal). Not kept over a session move, like lastCasts.
    private readonly PvpCooldownHold<SkillCastRules.LastCast> pvpCastHold = new(CastHoldEnd);
    private SkillCastRules.LastMotion? lastMotion;
    public readonly LagBudget Lag = new(); // casts and held-skill iterations that arrive early
    public readonly LagBudget MoveLag = new(); // position syncs that arrive early
    public readonly ClockRate CastClock = new(ClockRate.CastBoundMs, ClockRate.CastBackMs);
    public readonly ClockRate SyncClock = new(ClockRate.SyncBoundMs);
    // The log-only lines of the cast checks and of the attack point checks.
    public readonly LogGate CastLog = new();
    public readonly LogGate PointLog = new();
    private long heldCastUid;
    private long heldAttackSpeed;
    private readonly IDictionary<string, int> statLimits;

    public long DeathPenaltyEndTick {
        get => session.Player.Value.Character.DeathTick;
        private set => session.Player.Value.Character.DeathTick = value;
    }
    public short DeathCount {
        get => session.Player.Value.Character.DeathCount;
        private set => session.Player.Value.Character.DeathCount = value;
    }
    public int InstantReviveCount;
    private readonly StatAttributes statAttributes;
    private readonly SkillPoint skillPoints;
    public readonly IDictionary<int, int> GatheringCounts;
    public readonly IDictionary<int, int> GuideRecords;
    public int ExplorationProgress;

    public readonly SkillManager Skill;

    public ConfigManager(GameStorage.Request db, GameSession session) {
        this.session = session;
        keyBinds = new Dictionary<int, KeyBind>();
        hotBars = [];
        skillMacros = new List<SkillMacro>();
        lapenshards = new Dictionary<LapenshardSlot, int>();
        skillCooldowns = new Dictionary<int, SkillCooldown>();
        lastCasts = new Dictionary<int, SkillCastRules.LastCast>();

        (
            IList<KeyBind>? KeyBinds,
            IList<QuickSlot[]>? HotBars,
            IList<SkillMacro>? Macros,
            IList<Wardrobe>? Wardrobes,
            IList<int>? FavoriteStickers,
            IList<long>? FavoriteDesigners,
            IDictionary<LapenshardSlot, int>? Lapenshards,
            int InstantReviveCount,
            int ExplorationProgress,
            IDictionary<AttributePointSource, int>? StatPoints,
            IDictionary<BasicAttribute, int>? Allocation,
        SkillPoint? SkillPoint,
            IDictionary<int, int>? GatheringCounts,
            IDictionary<int, int>? GuideRecords,
        SkillBook? SkillBook
            ) load = db.LoadCharacterConfig(session.CharacterId);
        if (load.KeyBinds != null) {
            foreach (KeyBind keyBind in load.KeyBinds) {
                SetKeyBind(keyBind);
            }
        }
        Skill = new SkillManager(session, load.SkillBook ?? new SkillBook());

        for (int i = 0; i < TOTAL_HOT_BARS; i++) {
            hotBars.Add(new HotBar(load.HotBars?.ElementAtOrDefault(i)));
        }

        if (load.HotBars == null) {
            UpdateHotbarSkills();
        }

        skillMacros = load.Macros ?? new List<SkillMacro>();
        wardrobes = load.Wardrobes ?? new List<Wardrobe>();
        favoriteStickers = load.FavoriteStickers ?? new List<int>();
        favoriteDesigners = load.FavoriteDesigners ?? new List<long>();
        lapenshards = load.Lapenshards ?? new Dictionary<LapenshardSlot, int>();
        GatheringCounts = load.GatheringCounts ?? new Dictionary<int, int>();
        GuideRecords = load.GuideRecords ?? new Dictionary<int, int>();
        skillPoints = load.SkillPoint ?? new SkillPoint();
        ExplorationProgress = load.ExplorationProgress;
        // The day's paid revives, kept per character in character-config and cleared by the daily reset. The client
        // learns the count only from RevivalCount (CSceneStateGame::OnRevivalCount), so a login sends the stored one.
        InstantReviveCount = load.InstantReviveCount;

        statLimits = new Dictionary<string, int>() {
            { "StatPointLimit_str", Constants.StatPointLimit_str },
            { "StatPointLimit_dex", Constants.StatPointLimit_dex },
            { "StatPointLimit_int", Constants.StatPointLimit_int },
            { "StatPointLimit_luk", Constants.StatPointLimit_luk },
            { "StatPointLimit_hp", Constants.StatPointLimit_hp },
            { "StatPointLimit_cap", Constants.StatPointLimit_cap }
        };

        statAttributes = new StatAttributes(statLimits);
        if (load.StatPoints != null) {
            foreach ((AttributePointSource source, int amount) in load.StatPoints) {
                if (source == AttributePointSource.Prestige) {
                    statAttributes.Sources[source] = RefreshPrestigeAttributePoints();
                    continue;
                }
                statAttributes.Sources[source] = amount;
            }
        }
        if (load.Allocation != null) {
            // Reset points if total points exceed the limit.
            if (load.Allocation.Values.Sum() > statAttributes.TotalPoints) {
                load.Allocation.Clear();
            } else {
                foreach ((BasicAttribute attribute, int amount) in load.Allocation) {
                    statAttributes.Allocation[attribute] = amount;
                }
            }
        }
    }

    private int RefreshPrestigeAttributePoints() {
        IEnumerable<PrestigeLevelRewardMetadata> entries = session.TableMetadata.PrestigeLevelRewardTable.Entries.Values.Where(entry => entry.Level <= session.Player.Value.Account.PrestigeLevel && entry.Type == PrestigeAwardType.statPoint);
        return entries.Sum(entry => entry.Value);
    }

    public void UpdateHotbarSkills() {
        SortedDictionary<int, SkillInfo.Skill> skills = Skill.SkillInfo.GetMainLearnedJobSkills(SkillType.Active, SkillRank.Both);

        foreach (HotBar hotBar in hotBars) {
            foreach (QuickSlot quickSlot in hotBar.Slots) {
                if (quickSlot.SkillId > 0 && !skills.ContainsKey(quickSlot.SkillId)) {
                    hotBar.RemoveQuickSlot(quickSlot.SkillId, 0);
                }
            }
        }

        foreach ((int skillId, SkillInfo.Skill skill) in skills.Where(skill => skill.Value.Id > 10000000)) {
            if (hotBars[activeHotBar].FindQuickSlotIndex(skillId) == -1) {
                hotBars[activeHotBar].MoveQuickSlot(-1, new QuickSlot(skillId), false);
            }
        }
        session.Send(KeyTablePacket.LoadHotBar(activeHotBar, hotBars));
    }

    public void LoadKeyTable() {
        session.Send(keyBinds.Count == 0
            ? KeyTablePacket.LoadDefault()
            : KeyTablePacket.Load(KeyBindRules.ForSend(keyBinds.Values, GmCheck.IsGm(session.Player.Value.Account.AdminPermissions)), activeHotBar, hotBars));
    }

    public void LoadHotBars() {
        session.Send(KeyTablePacket.LoadHotBar(activeHotBar, hotBars));
    }

    public void InitMacros() {
        session.Send(SkillMacroPacket.Init(skillMacros));
    }

    public void LoadMacros() {
        session.Send(SkillMacroPacket.Load(skillMacros));
    }

    public void LoadWardrobe() {
        for (int i = 0; i < wardrobes.Count; i++) {
            session.Send(WardrobePacket.Load(i, wardrobes[i]));
        }
    }

    #region SkillCooldowns
    public void SetCacheSkillCooldowns(IList<SkillCooldownInfo> cooldowns, long currentTick) {
        lock (skillCooldowns) {
            foreach (SkillCooldownInfo cooldown in cooldowns) {
                skillCooldowns[cooldown.SkillId] = FromCache(cooldown, currentTick);
            }
        }
    }

    // The PvP-zone hold the cache kept; none after a World restart (an empty list, no stay).
    public void SetCachePvpHold(IList<SkillCooldownInfo> held, bool stay, long currentTick) {
        PvpHold.Load(held.Select(cooldown => FromCache(cooldown, currentTick)), cooldown => cooldown.SkillId, stay);
        pvpCastHold.Load([], cast => cast.SkillId, stay);
    }

    // The hold of the town cooldowns: a skill with recharge uses compares its uses out first.
    public static PvpCooldownHold<SkillCooldown> NewPvpHold() =>
        new(SettledEnd, cooldown => cooldown.RechargeMs > 0 ? cooldown.Charges : 0);

    // The tick a last cast stops holding back the next one at (its cooldown from the cast, at most).
    public static long CastHoldEnd(SkillCastRules.LastCast cast, long tick) => cast.ServerTick + cast.CooldownMs;

    public static SkillCooldown FromCache(SkillCooldownInfo cooldown, long currentTick) {
        return new SkillCooldown(cooldown.SkillId, (short) cooldown.SkillLevel) {
            EndTick = currentTick + cooldown.MsRemaining,
            GroupId = cooldown.GroupId,
            Charges = cooldown.Charges,
            RechargeMs = cooldown.RechargeMs,
        };
    }

    public static SkillCooldownInfo ToCache(SkillCooldown cooldown, long currentTick, long stopTime) {
        return new SkillCooldownInfo {
            SkillId = cooldown.SkillId,
            SkillLevel = cooldown.Level,
            GroupId = cooldown.GroupId,
            // Under 0 once it has ended: the World settles the recharge uses of the time away from there.
            MsRemaining = (int) Math.Clamp(cooldown.EndTick - currentTick, int.MinValue, int.MaxValue),
            StopTime = stopTime,
            Charges = cooldown.Charges,
            RechargeMs = cooldown.RechargeMs,
        };
    }

    // The end tick of a kept cooldown at this tick, its recharge uses settled.
    public static long SettledEnd(SkillCooldown cooldown, long tick) {
        if (cooldown.RechargeMs > 0) {
            (cooldown.EndTick, cooldown.Charges) = SkillCastRules.SettleRecharge(cooldown.EndTick, cooldown.Charges, cooldown.RechargeMs, tick);
        }
        return cooldown.EndTick;
    }
    public void SaveSkillCooldown(SkillMetadata skill, long startTick) {
        lock (skillCooldowns) {
            // An accepted cast always starts its cooldown again (it may come up to the slack early); a skill with
            // recharge uses keeps the one that runs while uses are still out (SkillCastRules.SettleRecharge).
            long lengthMs = CooldownMs(skill);
            bool recharge = skill.State.RechargeMaxCount > 0 && !skill.Data.Consume.UseItem;
            skillCooldowns.TryGetValue(skill.Id, out SkillCooldown? cooldown);
            if (recharge && cooldown != null) {
                ShortenRecharge(cooldown, lengthMs);
                (cooldown.EndTick, cooldown.Charges) = SkillCastRules.SettleRecharge(cooldown.EndTick, cooldown.Charges, cooldown.RechargeMs, startTick);
            }
            if (cooldown == null || !recharge || cooldown.Charges <= 0) {
                cooldown = new SkillCooldown(skill.Id, skill.Level) {
                    EndTick = lengthMs + startTick,
                    GroupId = skill.State.CooldownGroupId,
                    RechargeMs = recharge ? lengthMs : 0,
                };
                skillCooldowns[skill.Id] = cooldown;
            }
            if (skill.State.RechargeMaxCount > 0) {
                cooldown.Charges = Math.Clamp(cooldown.Charges + 1, cooldown.Charges, skill.State.RechargeMaxCount);
            }
        }
    }

    // The cooldown the client starts for a cast of the skill now (SkillCastRules.CooldownMs).
    private long CooldownMs(SkillMetadata skill) {
        (int invokeSeconds, float invokeRate) = session.Player.Buffs.GetInvokeValues(InvokeEffectType.ReduceCooldown, skill.Id, skill.Property.SkillGroup);
        return SkillCastRules.CooldownMs(skill.Data.Condition.CooldownTime, skill.State.IgnoreReduceCooldown,
            session.Player.Buffs.CooldownGroupRate(skill.State.CooldownGroupId),
            session.Player.Stats.Values[SpecialAttribute.ReduceCooldown].Multiplier(), invokeSeconds, invokeRate);
    }

    // Takes the length the client would start a recharge cooldown at now when it is shorter than the kept one
    // (SkillCastRules.ShortenRecharge).
    private static void ShortenRecharge(SkillCooldown cooldown, long lengthMs) {
        if (cooldown.RechargeMs > 0 && cooldown.Charges > 0) {
            (cooldown.EndTick, cooldown.RechargeMs) = SkillCastRules.ShortenRecharge(cooldown.EndTick, cooldown.RechargeMs, lengthMs);
        }
    }

    // Brings the recharge uses of the kept cooldowns up to this tick.
    private void SettleRecharges(long tick) {
        foreach (SkillCooldown cooldown in skillCooldowns.Values) {
            if (cooldown.RechargeMs > 0) {
                (cooldown.EndTick, cooldown.Charges) = SkillCastRules.SettleRecharge(cooldown.EndTick, cooldown.Charges, cooldown.RechargeMs, tick);
            }
        }
    }

    // The cooldown, the skill's own or one of its cooldown group, that refuses a use at this tick; null when none.
    public SkillCooldown? BlockingCooldown(SkillMetadata skill, long tick) {
        lock (skillCooldowns) {
            return SkillCastRules.BlockingCooldown(skillCooldowns.Values, skill.Id, skill.State.CooldownGroupId, tick);
        }
    }

    // Why a cast comes too early, null when it does not (SkillCastRules.TimingRefusal): before the motion of the
    // last accepted cast lets the next one in, or (cooldown: a job skill) before the cooldown of the last accepted
    // cast of it or of its cooldown group ends, at the length the client started (SkillCastRules.CooldownMs: the
    // group reductions and cooldown invoke effects of buffs and the caster's cooldown reduction). A skill with
    // recharge uses has its own check (RechargeShortfall). A cast that only arrives early draws once from the
    // session's lag budget, for the longer of the two.
    // clockTurnedFast: this cast's tick is the first of a fast time of the client's clock (to be logged).
    public string? CastTimingRefusal(SkillMetadata skill, bool cooldown, int castTick, long tick, out bool clockTurnedFast) {
        lock (skillCooldowns) {
            clockTurnedFast = CastClock.Add(castTick, tick);
            long motionMs = lastMotion is { } last
                ? SkillCastRules.MotionShortfall(last, skill.Id, skill.Property.ImmediateActive, AttackSpeed, castTick, tick) : 0;
            long cooldownMs = cooldown && skill.State.RechargeMaxCount <= 0
                ? SkillCastRules.CooldownShortfall(lastCasts.Values, skill.Id, skill.State.CooldownGroupId, castTick, tick, AttackSpeed) : 0;
            long rechargeMs = RechargeShortfall(skill, castTick, tick);
            cooldownMs = Math.Max(cooldownMs, rechargeMs);
            string? reason = SkillCastRules.TimingRefusal(Math.Max(motionMs, cooldownMs), CastClock.IsFast(tick), Lag, tick);
            return reason == null ? null : $"{(cooldownMs < motionMs ? "motion" : rechargeMs >= cooldownMs && rechargeMs > 0 ? "recharge" : "cooldown")}: {reason}";
        }
    }

    // The time a recharge skill's cast comes too early by: with no use left, or within the skill's gap after its
    // last use (SkillCastRules.RechargeEmptyShortfall, RechargeGapShortfall). Not checked for a skill that uses an
    // item (its cooldown is checked on its own) or one that counts its uses on its origin skill.
    private long RechargeShortfall(SkillMetadata skill, int castTick, long tick) {
        if (skill.State.RechargeMaxCount <= 0 || skill.State.RechargeShared || skill.Data.Consume.UseItem) {
            return 0;
        }
        if (!skillCooldowns.TryGetValue(skill.Id, out SkillCooldown? cooldown) || cooldown.RechargeMs <= 0) {
            return 0;
        }
        ShortenRecharge(cooldown, CooldownMs(skill));
        (long endTick, int usesOut) = SkillCastRules.SettleRecharge(cooldown.EndTick, cooldown.Charges, cooldown.RechargeMs, tick);
        if (usesOut <= 0) {
            return 0;
        }
        long gapMs = lastCasts.TryGetValue(skill.Id, out SkillCastRules.LastCast last)
            ? SkillCastRules.RechargeGapShortfall(skill.Data.Condition.RechargeGap, castTick, last.CastTick, tick, last.ServerTick) : 0;
        return Math.Max(gapMs, SkillCastRules.RechargeEmptyShortfall(usesOut, skill.State.RechargeMaxCount, endTick, tick));
    }

    public void SaveCastTime(SkillMetadata skill, SkillMetadataMotionProperty motion, int castTick, long tick) {
        lock (skillCooldowns) {
            // A cooldown under 1 s is not reduced (SkillCastRules.RequiredGap); one that a reduction brings under 1 s
            // is not checked. The check holds the cast to the length the client starts (CooldownMs); a share of the
            // caster's rate the server does not count (the game rule's, CEquipment::GetAbilityTotal) is not allowed for.
            long fullMs = (long) (skill.Data.Condition.CooldownTime * TimeSpan.FromSeconds(1).TotalMilliseconds);
            long cooldownMs = fullMs < 1000 ? fullMs : CooldownMs(skill);
            if (fullMs >= 1000 && cooldownMs < 1000) {
                cooldownMs = 0;
            }
            lastCasts[skill.Id] = new SkillCastRules.LastCast(skill.Id, skill.State.CooldownGroupId, cooldownMs, tick, castTick, AttackSpeed, motion.SequenceSpeed, motion.MoveType);
        }
    }

    private long AttackSpeed => session.Player.Stats.Values[BasicAttribute.AttackSpeed].Total;

    // Keeps an accepted cast's motion for the next cast. A cast whose motion has no sequence on the player's rig
    // leaves the last kept motion as it is.
    public void SaveCastMotion(SkillMetadataMotionProperty motion, int skillId, int castTick, long tick) {
        AnimationSequenceMetadata? sequence = null;
        session.Player.Animation.RigMetadata?.Sequences.TryGetValue(motion.SequenceName, out sequence);
        if (sequence == null || SkillCastRules.MotionKeys(sequence.Keys) is not { } keys) {
            return;
        }
        lastMotion = new SkillCastRules.LastMotion(skillId, keys.Next, keys.Self, motion.SequenceSpeed, AttackSpeed, tick, castTick, motion.MoveType);
    }

    // A cancelled cast's motion ends there: the next cast is not measured against it.
    public void ClearCastMotion(int skillId) {
        if (lastMotion?.SkillId == skillId) {
            lastMotion = null;
        }
    }

    // The shortest time between two iterations of a held skill with this motion (SkillHitRules.HeldInterval);
    // 0 when the motion's sequence has no loop on the player's rig. The highest attack speed seen during the cast
    // counts: the client may keep the loop's speed when a buff ends.
    public long HeldInterval(SkillMetadataMotionProperty motion, long castUid) {
        heldAttackSpeed = castUid == heldCastUid ? Math.Max(heldAttackSpeed, AttackSpeed) : AttackSpeed;
        heldCastUid = castUid;
        AnimationSequenceMetadata? sequence = null;
        session.Player.Animation.RigMetadata?.Sequences.TryGetValue(motion.SequenceName, out sequence);
        if (sequence == null) {
            return 0;
        }
        float? start = SkillCastRules.KeyTime(sequence.Keys, "loopstart");
        float? end = SkillCastRules.KeyTime(sequence.Keys, "loopend");
        if (start == null || end == null) {
            return 0;
        }
        return SkillHitRules.HeldInterval(end.Value - start.Value, SkillCastRules.MotionSpeed(motion.SequenceSpeed, heldAttackSpeed, motion.MoveType));
    }

    public SkillCooldown? GetSkillCooldown(int skillId) {
        lock (skillCooldowns) {
            return skillCooldowns.TryGetValue(skillId, out SkillCooldown? cooldown) ? cooldown : null;
        }
    }

    // Gives the skill count recharge uses back (an effect's <RemoveRechargingCount>, SkillCastRules.GiveBackRecharge)
    // and tells the client (min 0: chosen here, no data gives another). The record is the skill's own, as casts keep
    // it; the client takes the skill's recharge origin itself. None out drops the record, as the client ends the
    // cooldown.
    public void GiveBackRecharge(int skillId, int count) {
        lock (skillCooldowns) {
            if (skillCooldowns.TryGetValue(skillId, out SkillCooldown? cooldown) && cooldown.RechargeMs > 0) {
                (cooldown.EndTick, cooldown.Charges) = SkillCastRules.GiveBackRecharge(cooldown.EndTick, cooldown.Charges, cooldown.RechargeMs,
                    Environment.TickCount64, count);
                if (cooldown.Charges <= 0) {
                    skillCooldowns.Remove(skillId);
                }
            }
        }
        session.Send(SkillPacket.RechargeUses(skillId, 0, count));
    }

    public void SetSkillCooldown(int skillId, short level = 1, int endTick = 0) {
        lock (skillCooldowns) {
            var cooldown = new SkillCooldown(skillId, level) {
                EndTick = endTick,
            };
            skillCooldowns[skillId] = cooldown;
            // A reset cooldown lets the next cast come at once.
            lastCasts.Remove(skillId);

            session.Send(SkillPacket.Cooldown(cooldown));
        }
    }

    public void LoadSkillCooldowns() {
        lock (skillCooldowns) {
            SettleRecharges(Environment.TickCount64);
            if (session.Field is { } field) {
                // Entering a PvP zone ends the cooldowns of the skills that clear there (SkillCastRules.PvpZoneClears);
                // the town ones come back on the way out (PvpCooldownHold).
                List<int> cleared = SkillCastRules.PvpZoneClears(field.Metadata.Property.Type, skillCooldowns.Keys,
                    id => field.SkillMetadata.TryGet(id, skillCooldowns[id].Level, out SkillMetadata? skill) && skill.State.ClearCooldownOnPvpZone);
                PvpHold.EnterMap(field.Metadata.Property.Type == MapType.Pvp, skillCooldowns, cleared, Environment.TickCount64);
                pvpCastHold.EnterMap(field.Metadata.Property.Type == MapType.Pvp, lastCasts, cleared, Environment.TickCount64);
                foreach (int skillId in cleared) {
                    SetSkillCooldown(skillId, skillCooldowns[skillId].Level);
                }
            }
            foreach ((int skillId, SkillCooldown cooldown) in skillCooldowns) {
                if (Environment.TickCount64 > cooldown.EndTick) {
                    skillCooldowns.Remove(skillId);
                }
            }
            if (skillCooldowns.Count > 0) {
                session.Send(SkillPacket.Cooldown(skillCooldowns.Values.ToArray()));
            }
        }
    }

    public IList<SkillCooldown> GetCurrentSkillCooldowns() {
        lock (skillCooldowns) {
            IList<SkillCooldown> cooldowns = new List<SkillCooldown>();
            SettleRecharges(Environment.TickCount64);
            foreach ((int skillId, SkillCooldown cooldown) in skillCooldowns) {
                if (Environment.TickCount64 > cooldown.EndTick) {
                    continue;
                }
                cooldowns.Add(cooldown);
            }
            return cooldowns;
        }
    }
    #endregion

    #region PremiumClub
    // The VIP end time after adding hours: from now when the VIP time has ended, else from its end.
    public static long PremiumTimeAfter(long premiumTime, long hours, DateTime now) =>
        premiumTime < now.ToEpochSeconds()
            ? now.AddHours(hours).ToEpochSeconds()
            : Math.Min(premiumTime.FromEpochSeconds().AddHours(hours).ToEpochSeconds(), long.MaxValue);

    public void UpdatePremiumTime(long hours) {
        bool started = !IsPremiumClubActive();
        session.Player.Value.Account.PremiumTime = PremiumTimeAfter(session.Player.Value.Account.PremiumTime, hours, DateTime.Now);
        PremiumTimeChanged(started);
    }

    // What follows a new account VIP time (already set): the notice, the VIP packet and the copies of the time.
    // started: the VIP time had ended before.
    public void PremiumTimeChanged(bool started) {
        if (started) {
            session.Dungeon.UpdateDungeonEnterLimit();
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, StringCode.s_vip_coupon_new_msg));
        } else {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, StringCode.s_vip_coupon_extend_msg));
        }

        session.Send(PremiumCubPacket.Activate(session.Player.ObjectId, session.Player.Value.Account.PremiumTime));
        session.Player.Value.Character.PremiumTime = session.Player.Value.Account.PremiumTime;
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            PremiumTime = session.Player.Value.Account.PremiumTime,
            Async = true,
        });
    }

    public void RefreshPremiumClubBuffs() {
        if (session.Player.Value.Account.PremiumTime > DateTime.Now.ToEpochSeconds() && session.Field is not null) {
            foreach ((int buffId, PremiumClubTable.Buff buff) in session.TableMetadata.PremiumClubTable.Buffs) {
                session.Player.Buffs.AddBuff(session.Player, session.Player, buff.Id, buff.Level, session.Field.FieldTick);
            }
        }
    }

    public bool IsPremiumClubActive() => session.Player.Value.Account.PremiumTime > DateTime.Now.ToEpochSeconds();
    #endregion

    #region ChatStickers
    public void LoadChatStickers() {
        List<ChatSticker> stickers = [];
        foreach (KeyValuePair<int, long> set in session.Player.Value.Unlock.StickerSets) {
            stickers.Add(new(set.Key, set.Value));
        }
        session.Send(ChatStickerPacket.Load(favoriteStickers.ToList(), stickers));
    }

    public bool TryFavoriteChatSticker(int stickerId) {
        if (favoriteStickers.Contains(stickerId)) {
            return false;
        }

        favoriteStickers.Add(stickerId);
        return true;
    }

    // Takes out every favourite the test picks; returns the ones taken out.
    public List<int> RemoveFavoriteChatStickers(Func<int, bool> match) {
        List<int> removed = favoriteStickers.Where(match).ToList();
        foreach (int stickerId in removed) {
            favoriteStickers.Remove(stickerId);
        }
        return removed;
    }

    public bool TryUnfavoriteChatSticker(int stickerId) {
        if (!favoriteStickers.Contains(stickerId)) {
            return false;
        }

        favoriteStickers.Remove(stickerId);
        return true;
    }
    #endregion

    public void LoadStatAttributes() {
        session.Send(AttributePointPacket.Sources(statAttributes));
        session.Send(AttributePointPacket.Allocation(statAttributes));
    }

    public void AddStatPoint(AttributePointSource source, int amount) {
        statAttributes.Sources[source] += amount;
        session.Send(AttributePointPacket.Sources(statAttributes));
    }

    public void LoadSkillPoints() {
        session.Send(SkillPointPacket.Sources(skillPoints));
    }

    // The extra skill points of each rank (0 base, 1 awakening) summed over their sources, as the client adds them
    // to the points from the level (SceneStateGame OnSkillPoint).
    public int[] ExtraSkillPoints() {
        var result = new int[SkillPoint.RANKS];
        foreach (SkillPoint.PointRank point in skillPoints.Points.Values) {
            foreach ((short rank, int amount) in point.Ranks) {
                if (rank >= 0 && rank < result.Length) {
                    result[rank] += amount;
                }
            }
        }
        return result;
    }

    public void AddSkillPoint(SkillPointSource source, int amount, short rank = 0) {
        skillPoints[source][rank] += amount;
        session.Send(SkillPointPacket.Sources(skillPoints));
    }

    public IList<long> GetFavoriteDesigners() {
        return favoriteDesigners;
    }

    public void AddFavoriteDesigner(long designer) {
        favoriteDesigners.Add(designer);
    }

    public void RemoveFavoriteDesigner(long designer) {
        favoriteDesigners.Remove(designer);
    }

    public void LoadRevival() {
        if (session.Player.Field.FieldTick > DeathPenaltyEndTick) {
            DeathPenaltyEndTick = 0;
            DeathCount = 0;
        }
        session.Send(RevivalPacket.RevivalCount(InstantReviveCount));
        session.Send(RevivalPacket.UpdatePenalty(session.Player.ObjectId, (int) DeathPenaltyEndTick, DeathCount));
    }

    /// <summary>
    /// Updates the death penalty for the player
    /// </summary>
    /// <param name="endTick">The tick when the penalty ends, or 0 to reset</param>
    public void UpdateDeathPenalty(long endTick) {
        // No penalty for low level players; the clear of a level up is sent at every level, as KMS2 sends it.
        if (endTick != 0 && session.Player.Value.Character.Level < Constants.UserRevivalPaneltyMinLevel) {
            return;
        }

        // Reset penalty if endTick is 0
        if (endTick == 0) {
            DeathCount = 0;
            DeathPenaltyEndTick = 0;
        }
        // Otherwise update penalty
        else {
            // Reset count if previous penalty expired
            if (session.Field?.FieldTick > DeathPenaltyEndTick) {
                DeathCount = 0;
            }
            DeathCount++;
            DeathPenaltyEndTick = endTick;
        }

        // Send update to client
        session.Send(RevivalPacket.UpdatePenalty(session.Player.ObjectId, (int) DeathPenaltyEndTick, DeathCount));
    }

    public void SetDeathPenalty(DeathInfo deathInfo, long currentTick) {
        DeathPenaltyEndTick = currentTick + deathInfo.MsRemaining;
        DeathCount = (short) deathInfo.Count;
    }

    public void AddInstantReviveCount(int count = 1) {
        if (count < 0) {
            InstantReviveCount = 0;
        } else {
            InstantReviveCount += count;
        }

        session.Send(RevivalPacket.RevivalCount(InstantReviveCount));
    }

    #region KeyBind
    public int KeyBindCount => keyBinds.Count;

    public bool HasKeyBind(int keyCode) {
        return keyBinds.ContainsKey(keyCode);
    }

    public void SetKeyBind(in KeyBind keyBind) {
        keyBinds[keyBind.KeyCode] = keyBind;
    }
    #endregion

    #region HotBar
    public void SetActiveHotBar(short hotBarId) {
        if (hotBarId < 0 || hotBarId >= hotBars.Count) {
            return;
        }

        activeHotBar = hotBarId;
    }

    public bool TryGetHotBar(short hotBarId, [NotNullWhen(true)] out HotBar? hotBar) {
        if (hotBarId < 0 || hotBarId >= hotBars.Count) {
            hotBar = null;
            return false;
        }

        hotBar = hotBars[hotBarId];
        return true;
    }
    #endregion

    #region Macros
    public void UpdateMacros(List<SkillMacro> updated) {
        skillMacros = updated;
    }
    #endregion

    #region Wardrobe
    public bool AddWardrobe(string name) {
        if (wardrobes.Count >= Constant.MaxClosetMaxCount) {
            return false;
        }
        if (name.Length > Constant.MaxClosetTabNameLength) {
            return false;
        }

        wardrobes.Add(new Wardrobe(0, name));
        return true;
    }

    public bool TryGetWardrobe(int index, [NotNullWhen(true)] out Wardrobe? wardrobe) {
        if (index < 0 || index >= wardrobes.Count) {
            wardrobe = null;
            return false;
        }

        wardrobe = wardrobes[index];
        return true;
    }
    #endregion

    #region StatPoints
    public void AllocateStatPoint(BasicAttribute type, int amount) {
        if (amount <= 0) {
            return;
        }

        // Invalid stat type.
        if (StatAttributes.PointAllocation.StatLimit(type, statLimits) <= 0) {
            return;
        }

        // No points remaining.
        if (statAttributes.UsedPoints >= statAttributes.TotalPoints) {
            return;
        }

        amount = Math.Min(amount, statAttributes.TotalPoints - statAttributes.UsedPoints);

        // Reached limit for allocation.
        if (session.Config.statAttributes.Allocation[type] + amount > StatAttributes.PointAllocation.StatLimit(type, statLimits)) {
            session.Send(NoticePacket.Message("s_char_info_limit_stat_point"));
            return;
        }

        session.Config.statAttributes.Allocation[type] += amount;
        // KMS2 sends the allocation, then the whole stat block: the spent point also moves the attack and resistance.
        session.Send(AttributePointPacket.Allocation(session.Config.statAttributes));
        session.Stats.Refresh();
    }

    public void ResetStatPoints() {
        // The client sends a reset only with points spent (CStatPointManager::RequestReset).
        if (statAttributes.UsedPoints == 0) {
            return;
        }

        foreach (BasicAttribute type in session.Config.statAttributes.Allocation.Attributes) {
            session.Config.statAttributes.Allocation[type] = 0;
        }

        // KMS2 order: the allocation, the notice, then the stat block.
        session.Send(AttributePointPacket.Allocation(session.Config.statAttributes));
        session.Send(NoticePacket.Message("s_char_info_reset_stat_pointsuccess_msg"));
        session.Stats.Refresh();
    }

    // The spent points are not kept in the stats: every rebuild (StatsManager.Refresh) adds them again.
    public void AddStatAllocation(Maple2.Server.Game.Model.Stats values) {
        foreach (BasicAttribute type in statAttributes.Allocation.Attributes) {
            values.AddAllocation(type, statAttributes.Allocation[type]);
        }
    }
    #endregion

    #region Lapenshard
    public void LoadLapenshard() {
        session.Send(LapenshardPacket.Load(lapenshards));
    }

    public int TryGetLapenshard(LapenshardSlot slot) {
        return lapenshards.TryGetValue(slot, out int id) ? id : -1;
    }

    public void SetLapenshard(LapenshardSlot slot, int id) {
        lapenshards[slot] = id;
    }

    #region CharacterAbility
    private Character AbilityCharacter => session.Player.Value.Character;

    // Bonus points have no source in the client, so none are given.
    public void LoadCharacterAbilities() {
        session.Send(CharacterAbilityPacket.Load(AbilityCharacter.Abilities, AbilityCharacter.AbilityResetTime, 0));
    }

    // CharacterAbility mode 2.
    public bool LearnCharacterAbility(int abilityId) {
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        Character character = AbilityCharacter;
        bool questDone = session.Quest.TryGetQuest(constants.CharacterAbilityOpenQuestID, out Quest? quest) && quest.State == QuestState.Completed;
        if (!CharacterAbilityRules.IsOpen(character.Level, questDone)) {
            return false;
        }

        int requireLevel = session.TableMetadata.CharacterAbilityTable.Entries.TryGetValue(abilityId, out CharacterAbilityTable.Entry? entry) ? entry.RequireLevel : -1;
        int free = CharacterAbilityRules.FreePoints(character.Level, constants.CharacterAbilityDefaultPoint, 0, character.Abilities.Count);
        if (!CharacterAbilityRules.CanLearn(requireLevel, character.Abilities.Contains(abilityId), character.Level, free)) {
            return false;
        }

        character.Abilities.Add(abilityId);
        session.Send(CharacterAbilityPacket.Learn(abilityId));
        RefreshCharacterAbilityBuffs();
        // Trophies 23100216-23100220: character_ability_learn, target = the ability's category.
        session.ConditionUpdate(ConditionType.character_ability_learn, targetLong: entry!.CategoryId);
        return true;
    }

    // CharacterAbility mode 3 (GM only, checked by the handler): learns every table ability, then sends the
    // load packet with the whole list. Chosen here: no trophy counts, as for a GM tool.
    public void LearnAllCharacterAbilities() {
        Character character = AbilityCharacter;
        List<int> ids = CharacterAbilityRules.LearnAllIds(session.TableMetadata.CharacterAbilityTable.Entries.Keys, character.Abilities);
        character.Abilities.AddRange(ids);
        LoadCharacterAbilities();
        RefreshCharacterAbilityBuffs();
    }

    // CharacterAbility mode 1: for CharacterAbilityResetMeso mesos, once per CharacterAbilityResetCoolTimeMinute.
    public bool ResetCharacterAbilities() {
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        Character character = AbilityCharacter;
        long now = DateTime.Now.ToEpochSeconds();
        long wait = CharacterAbilityRules.ResetWait(character.AbilityResetTime, now, constants.CharacterAbilityResetCoolTimeMinute);
        long price = constants.CharacterAbilityResetMeso;
        if (CharacterAbilityRules.CanReset(character.Abilities.Count, wait, session.Currency.Meso, price) != CharacterAbilityRules.ResetStep.Reset) {
            return false;
        }

        // KMS2 order: the meso, the load packet with the empty list and the reset time, the trophy, then one
        // buff removal per ability. It sends no mode 1 answer (the client's reader for it is empty).
        session.Currency.ChangeMeso(-price);
        int[] effectIds = character.Abilities
            .Select(abilityId => session.TableMetadata.CharacterAbilityTable.Entries.TryGetValue(abilityId, out CharacterAbilityTable.Entry? entry) ? entry.AdditionalEffectId : 0)
            .Where(effectId => effectId > 0).ToArray();
        character.Abilities.Clear();
        character.AbilityResetTime = now;
        LoadCharacterAbilities();
        // Trophy 23100221: character_ability_reset, no target.
        session.ConditionUpdate(ConditionType.character_ability_reset);
        foreach (int effectId in effectIds) {
            session.Player.Buffs.Remove(effectId, session.Player.ObjectId);
        }
        return true;
    }

    // The cool time reset stone (ResetAbilityCoolTime): consume runs only when the stone may be used; the reset time
    // goes back to 0 ("never reset", CharacterAbilityRules.ResetWait) and the load packet carries it to the window.
    public bool ResetCharacterAbilityCoolTime(Func<bool> consume) {
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        Character character = AbilityCharacter;
        bool questDone = session.Quest.TryGetQuest(constants.CharacterAbilityOpenQuestID, out Quest? quest) && quest.State == QuestState.Completed;
        long wait = CharacterAbilityRules.ResetWait(character.AbilityResetTime, DateTime.Now.ToEpochSeconds(), constants.CharacterAbilityResetCoolTimeMinute);
        if (!CharacterAbilityRules.CanResetCoolTime(CharacterAbilityRules.IsOpen(character.Level, questDone), wait) || !consume()) {
            return false;
        }

        character.AbilityResetTime = 0;
        LoadCharacterAbilities();
        return true;
    }

    // Gives the effects of the learned abilities that the player does not have
    // (CharacterAbilityRules.MissingEffects). Called on learn and at every field entry.
    public void RefreshCharacterAbilityBuffs() {
        if (session.Field is not { } field) {
            return;
        }

        IReadOnlyDictionary<int, CharacterAbilityTable.Entry> entries = session.TableMetadata.CharacterAbilityTable.Entries;
        foreach ((int id, short level) in CharacterAbilityRules.MissingEffects(AbilityCharacter.Abilities,
                     abilityId => entries.TryGetValue(abilityId, out CharacterAbilityTable.Entry? entry) ? (entry.AdditionalEffectId, entry.AdditionalEffectLevel) : null,
                     (id, level) => session.Player.Buffs.HasBuff(id, level))) {
            if (field.SkillMetadata.TryGetEffect(id, level, out _)) {
                session.Player.Buffs.AddBuff(session.Player, session.Player, id, level, field.FieldTick);
            }
        }
    }
    #endregion

    // Gives the effects of the worn lapenshards that the player does not have (LapenshardRules.MissingEffects).
    // Called when a shard is put on or taken off and at every field entry.
    public void RefreshLapenshardBuffs() {
        if (session.Field is not { } field) {
            return;
        }

        foreach ((int id, short level) in LapenshardRules.MissingEffects(lapenshards.Values.ToArray(),
                     itemId => session.ItemMetadata.TryGet(itemId, out ItemMetadata? metadata) ? metadata.AdditionalEffects : null,
                     (id, level) => session.Player.Buffs.HasBuff(id, level))) {
            if (field.SkillMetadata.TryGetEffect(id, level, out _)) {
                session.Player.Buffs.AddBuff(session.Player, session.Player, id, level, field.FieldTick);
            }
        }
    }

    public bool EquipLapenshard(long itemUid, LapenshardSlot slot) {
        if (!Enum.IsDefined<LapenshardSlot>(slot)) {
            return false;
        }

        Item? lapenshard = session.Item.Inventory.Get(itemUid);
        if (lapenshard == null) {
            session.Send(NoticePacket.MessageBox(StringCode.s_item_invalid_do_not_have));
            return false;
        }
        if (!lapenshard.Type.IsLapenshard) {
            return false;
        }
        StringCode result = EquipManager.ValidateEquipItem(session, lapenshard);
        if (result != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(result));
            return false;
        }

        // Cannot equip the same lapenshard twice.
        foreach (LapenshardSlot lapenshardSlot in Enum.GetValues<LapenshardSlot>()) {
            if (lapenshards.TryGetValue(lapenshardSlot, out int existing) && existing == lapenshard.Id) {
                return false;
            }
        }

        if (lapenshard.Type.IsRedLapenshard && slot is not (LapenshardSlot.Red1 or LapenshardSlot.Red2)) {
            return false;
        }
        if (lapenshard.Type.IsBlueLapenshard && slot is not (LapenshardSlot.Blue1 or LapenshardSlot.Blue2)) {
            return false;
        }
        if (lapenshard.Type.IsGreenLapenshard && slot is not (LapenshardSlot.Green1 or LapenshardSlot.Green2)) {
            return false;
        }

        if (!UnequipLapenshard(slot)) {
            return false;
        }
        // The config with the shard worn and the bag row without it are stored together or not at all, as for the
        // unequip: the bag's change was left to the next session save, so an equip and an unequip, then a crash or a
        // failed save, left the old bag row beside the unequip's new one (two shards after the next login).
        lapenshards[slot] = lapenshard.Id;
        if (!session.TrySavePurchase(db => {
                Save(db);
                return session.Item.Inventory.SaveConsumed(db, itemUid, 1);
            })) {
            lapenshards.Remove(slot);
            return false;
        }
        if (!session.Item.Inventory.ConsumeStored(itemUid, 1)) {
            // Checked above on the same thread; the stored rows hold the equip, as the next login reads it.
            Serilog.Log.Logger.ForContext<ConfigManager>().Error("Lapenshard {ItemUid} stored as worn but not taken from the bag of {CharacterId}", itemUid, session.CharacterId);
        }

        session.Send(LapenshardPacket.Equip(slot, lapenshard.Id));
        RefreshLapenshardBuffs();

        return true;
    }

    public bool UnequipLapenshard(LapenshardSlot slot) {
        if (!Enum.IsDefined<LapenshardSlot>(slot)) {
            return false;
        }

        if (!lapenshards.TryGetValue(slot, out int lapenshardId)) {
            return true; // Nothing to unequip
        }

        Item? lapenshard = session.Field?.ItemDrop.CreateItem(lapenshardId, Constant.LapenshardGrade);
        if (lapenshard == null) {
            return false;
        }

        lapenshard.Transfer?.Bind(session.Player.Value.Character);
        if (!session.Item.Inventory.CanAdd(lapenshard)) {
            session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
            return false;
        }

        if (!lapenshards.Remove(slot, out lapenshardId)) {
            return false;
        }

        // The config without the shard and the shard's item row are stored together or not at all. The item row used
        // to be written at once (Inventory.Add) and the config only by the next session save, so a crash or a failed
        // save in between left the shard both worn and in the bag after the next login.
        lapenshard.Slot = -1;
        Item? stored = null;
        if (!session.TrySavePurchase(db => {
                Save(db);
                stored = db.CreateItem(session.CharacterId, lapenshard);
                return stored != null;
            }) || stored == null) {
            lapenshards[slot] = lapenshardId; // Revert removal
            return false;
        }
        if (!session.Item.Inventory.Add(stored)) {
            // The room was checked above; the stored row is the character's bag item, so the next login has it.
            Serilog.Log.Logger.ForContext<ConfigManager>().Error("Lapenshard {ItemId} stored but not added to the bag of {CharacterId}", lapenshardId, session.CharacterId);
        }

        session.Send(LapenshardPacket.Unequip(slot));
        // The shard's effects end with it; one that another worn shard gives too is given again.
        if (session.ItemMetadata.TryGet(lapenshardId, out ItemMetadata? unequipped)) {
            session.Player.Buffs.Remove(unequipped.AdditionalEffects.Select(effect => (effect.Id, session.Player.ObjectId)).ToArray());
        }
        RefreshLapenshardBuffs();
        return true;
    }
    #endregion

    public void Save(GameStorage.Request db) {
        db.SaveCharacterConfig(
            session.CharacterId, keyBinds.Values.ToList(),
            hotBars.Select(hotBar => hotBar.Slots).ToList(),
            skillMacros,
            wardrobes,
            favoriteStickers,
            favoriteDesigners,
            lapenshards,
            InstantReviveCount,
            ExplorationProgress,
            statAttributes.Allocation,
            statAttributes.Sources,
            skillPoints,
            GatheringCounts,
            GuideRecords,
            Skill.SkillBook
        );
    }
}

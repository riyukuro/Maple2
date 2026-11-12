using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
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
    private readonly IDictionary<int, SkillCastRules.LastCast> lastCasts;
    private SkillCastRules.LastMotion? lastMotion;
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
        foreach (SkillCooldownInfo cooldown in cooldowns) {
            skillCooldowns[cooldown.SkillId] = new SkillCooldown(cooldown.SkillId, (short) cooldown.SkillLevel) {
                EndTick = currentTick + cooldown.MsRemaining,
                GroupId = cooldown.GroupId,
            };
        }
    }
    public void SaveSkillCooldown(SkillMetadata skill, long startTick) {
        // An accepted cast always starts its cooldown again (it may come up to the slack early); a skill with
        // recharge uses keeps the one that runs.
        if (!skillCooldowns.TryGetValue(skill.Id, out SkillCooldown? cooldown) || startTick > cooldown.EndTick || skill.Data.Consume.UseItem
            || skill.State.RechargeMaxCount <= 0) {
            cooldown = new SkillCooldown(skill.Id, skill.Level) {
                EndTick = (long) (skill.Data.Condition.CooldownTime * TimeSpan.FromSeconds(1).TotalMilliseconds) + startTick,
                GroupId = skill.State.CooldownGroupId,
            };
            skillCooldowns[skill.Id] = cooldown;
        }
        if (skill.State.RechargeMaxCount > 0) {
            cooldown.Charges = Math.Clamp(cooldown.Charges + 1, cooldown.Charges, skill.State.RechargeMaxCount);
        }
    }

    // The cooldown, the skill's own or one of its cooldown group, that refuses a use at this tick; null when none.
    public SkillCooldown? BlockingCooldown(SkillMetadata skill, long tick) {
        return SkillCastRules.BlockingCooldown(skillCooldowns.Values, skill.Id, skill.State.CooldownGroupId, tick);
    }

    // Whether a cast of a job skill comes too early after the last accepted cast of it or of its cooldown group
    // (SkillCastRules.CooldownRefuses). Not checked, because the server does not know the cooldown's length then:
    // a skill with recharge uses, and a cast whose cooldown a buff or the caster's cooldown reduction shortens.
    public bool CastTooEarly(SkillMetadata skill, int castTick, long tick) {
        if (skill.State.RechargeMaxCount > 0 || CooldownReduced(skill)) {
            return false;
        }
        return SkillCastRules.CooldownRefuses(lastCasts.Values, skill.Id, skill.State.CooldownGroupId, castTick, tick);
    }

    private bool CooldownReduced(SkillMetadata skill) {
        if (skill.State.IgnoreReduceCooldown) {
            return false;
        }
        if (session.Player.Stats.Values[SpecialAttribute.ReduceCooldown].Total != 0) {
            return true;
        }
        (int value, float rate) = session.Player.Buffs.GetInvokeValues(InvokeEffectType.ReduceCooldown, skill.Id, skill.Property.SkillGroup);
        return value != 0 || rate != 0;
    }

    public void SaveCastTime(SkillMetadata skill, int castTick, long tick) {
        long cooldownMs = (long) (skill.Data.Condition.CooldownTime * TimeSpan.FromSeconds(1).TotalMilliseconds);
        lastCasts[skill.Id] = new SkillCastRules.LastCast(skill.Id, skill.State.CooldownGroupId, cooldownMs, tick, castTick);
    }

    private long AttackSpeed => session.Player.Stats.Values[BasicAttribute.AttackSpeed].Total;

    // Whether a cast comes before the motion of the last accepted cast lets the next one in
    // (SkillCastRules.MotionRefuses).
    public bool MotionTooEarly(SkillMetadata skill, int castTick, long tick) {
        return lastMotion is { } last
               && SkillCastRules.MotionRefuses(last, skill.Id, skill.Property.ImmediateActive, AttackSpeed, castTick, tick);
    }

    // Keeps an accepted cast's motion for the next cast. A cast whose motion has no sequence on the player's rig
    // leaves the last kept motion as it is.
    public void SaveCastMotion(SkillMetadataMotionProperty motion, int skillId, int castTick, long tick) {
        AnimationSequenceMetadata? sequence = null;
        session.Player.Animation.RigMetadata?.Sequences.TryGetValue(motion.SequenceName, out sequence);
        if (sequence == null || SkillCastRules.MotionKeys(sequence.Keys) is not { } keys) {
            return;
        }
        lastMotion = new SkillCastRules.LastMotion(skillId, keys.Next, keys.Self, motion.SequenceSpeed, AttackSpeed, tick, castTick);
    }

    // The shortest time between two iterations of a held skill with this motion (SkillHitRules.HeldInterval);
    // 0 when the motion's sequence has no loop on the player's rig.
    public long HeldInterval(SkillMetadataMotionProperty motion) {
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
        return SkillHitRules.HeldInterval(end.Value - start.Value, SkillCastRules.MotionSpeed(motion.SequenceSpeed, AttackSpeed));
    }

    public SkillCooldown? GetSkillCooldown(int skillId) {
        return skillCooldowns.TryGetValue(skillId, out SkillCooldown? cooldown) ? cooldown : null;
    }

    public void SetSkillCooldown(int skillId, short level = 1, int endTick = 0) {
        var cooldown = new SkillCooldown(skillId, level) {
            EndTick = endTick,
        };
        skillCooldowns[skillId] = cooldown;
        // A reset cooldown lets the next cast come at once.
        lastCasts.Remove(skillId);

        session.Send(SkillPacket.Cooldown(cooldown));
    }

    public void LoadSkillCooldowns() {
        foreach ((int skillId, SkillCooldown cooldown) in skillCooldowns) {
            if (Environment.TickCount64 > cooldown.EndTick) {
                skillCooldowns.Remove(skillId);
            }
        }
        if (skillCooldowns.Count > 0) {
            session.Send(SkillPacket.Cooldown(skillCooldowns.Values.ToArray()));
        }
    }

    public IList<SkillCooldown> GetCurrentSkillCooldowns() {
        IList<SkillCooldown> cooldowns = new List<SkillCooldown>();
        foreach ((int skillId, SkillCooldown cooldown) in skillCooldowns) {
            if (Environment.TickCount64 > cooldown.EndTick) {
                continue;
            }
            cooldowns.Add(cooldown);
        }
        return cooldowns;
    }
    #endregion

    #region PremiumClub
    public void UpdatePremiumTime(long hours) {
        if (session.Player.Value.Account.PremiumTime < DateTime.Now.ToEpochSeconds()) {
            session.Player.Value.Account.PremiumTime = DateTime.Now.AddHours(hours).ToEpochSeconds();
            session.Dungeon.UpdateDungeonEnterLimit();
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, StringCode.s_vip_coupon_new_msg));
        } else {
            session.Player.Value.Account.PremiumTime = Math.Min(session.Player.Value.Account.PremiumTime.FromEpochSeconds().AddHours(hours).ToEpochSeconds(), long.MaxValue);
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
        // Skip penalty for low level players
        if (session.Player.Value.Character.Level < Constants.UserRevivalPaneltyMinLevel) {
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
        foreach (BasicAttribute type in session.Config.statAttributes.Allocation.Attributes) {
            session.Config.statAttributes.Allocation[type] = 0;
        }

        session.Send(AttributePointPacket.Allocation(session.Config.statAttributes));
        session.Stats.Refresh();
        session.Send(NoticePacket.Message("s_char_info_reset_stat_pointsuccess_msg"));
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

        session.Currency.Meso -= price;
        foreach (int abilityId in character.Abilities) {
            if (session.TableMetadata.CharacterAbilityTable.Entries.TryGetValue(abilityId, out CharacterAbilityTable.Entry? entry) && entry.AdditionalEffectId > 0) {
                session.Player.Buffs.Remove(entry.AdditionalEffectId, session.Player.ObjectId);
            }
        }
        character.Abilities.Clear();
        character.AbilityResetTime = now;
        LoadCharacterAbilities();
        session.Send(CharacterAbilityPacket.Reset());
        // Trophy 23100221: character_ability_reset, no target.
        session.ConditionUpdate(ConditionType.character_ability_reset);
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

        if (!session.Item.Inventory.Consume(itemUid, 1)) {
            return false;
        }

        lapenshards[slot] = lapenshard.Id;
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

        if (!lapenshards.Remove(slot, out lapenshardId)) {
            return false;
        }

        lapenshard.Transfer?.Bind(session.Player.Value.Character);
        bool success = session.Item.Inventory.Add(lapenshard);
        if (!success) {
            lapenshards[slot] = lapenshardId; // Revert removal
            return false;
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

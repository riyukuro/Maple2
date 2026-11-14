using Maple2.Database.Extensions;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class StatsManager {
    public IActor Actor { get; private set; }
    // The level stats of every job, and the table of the job the stats were last built for (levelStatsJob): a job
    // change (the GM job command) picks the new job's table at the next Refresh. Before, the table stayed the one of
    // the job at session start until a channel change, so a Heavy Gunner made a Priest kept SpRegen 0 every 10 s.
    private readonly IReadOnlyDictionary<JobCode, IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>> jobStats =
        new Dictionary<JobCode, IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>>();
    private IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>> levelStats;
    private JobCode levelStatsJob;
    // The effect ids last given by set bonuses (RefreshSetEffects).
    private readonly HashSet<int> setEffectIds = [];

    public Stats Values { get; init; }

    public StatsManager(IActor actor, UserStatTable userStatsMetadata) {
        Actor = actor;
        levelStats = new Dictionary<short, IReadOnlyDictionary<BasicAttribute, long>>();

        if (actor is not FieldPlayer player) {
            Values = new Stats(new Dictionary<BasicAttribute, long>(), 0);
            return;
        }
        if (!userStatsMetadata.JobStats.TryGetValue(player.Value.Character.Job.Code(), out IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>? stats)) {
            throw new Exception("Failed to initialize StatsManager, could not find metadata for job " + player.Value.Character.Job.Code() + ".");
        }

        jobStats = userStatsMetadata.JobStats;
        levelStats = stats;
        levelStatsJob = player.Value.Character.Job.Code();
        if (!stats.TryGetValue(player.Value.Character.Level, out IReadOnlyDictionary<BasicAttribute, long>? metadata)) {
            throw new Exception("Failed to initialize StatsManager, could not find metadata for level " + player.Value.Character.Level + ".");
        }
        Values = new Stats(metadata, player.Value.Character.Job.Code());
    }

    public void SetBattleMountStats(IReadOnlyDictionary<BasicAttribute, long> stats) {
        Values.SetStaticStats(stats);
    }

    // An npc's own stats (its data's, or the room's player count factor of them): what a rebuild starts from.
    private IReadOnlyDictionary<BasicAttribute, long>? npcStats;

    /// <summary>
    /// Sets an npc's own stats (the room's player count factor) and rebuilds its totals on them.
    /// </summary>
    public void SetNpcStats(IReadOnlyDictionary<BasicAttribute, long> stats) {
        npcStats = stats;
        Values.SetStaticStats(stats);
        Refresh();
    }

    public StatsManager(IActor actor) {
        Actor = actor;
        levelStats = new Dictionary<short, IReadOnlyDictionary<BasicAttribute, long>>();

        if (actor is FieldNpc npc) {
            Values = new Stats(npc.Value.Metadata.Stat);
            npcStats = npc.Value.Metadata.Stat.Stats;
            return;
        }

        Values = new Stats(new Dictionary<BasicAttribute, long>(), 0);
    }

    /// <summary>
    /// Get the actor's bonus attack min and max.
    /// </summary>
    /// <param name="targetBonusAttackResistance">Target's total Bonus Attack resistance</param>
    /// <param name="targetMaxWeaponAttackResistance">Target's total Max Weapon Attack resistance</param>
    /// <returns>Tuple. Item1 = Min Damage. Item2 = Max Damage.</returns>
    public (double, double) GetBonusAttack(float targetBonusAttackResistance, float targetMaxWeaponAttackResistance) {
        switch (Actor) {
            case FieldPlayer player:
                double bonusAttack = Values[BasicAttribute.BonusAtk].Total + Constant.PetAttackMultiplier * Values[BasicAttribute.PetBonusAtk].Total;
                double bonusAttackResistance = 1 / (1 + targetBonusAttackResistance);
                double weaponAttackWeakness = 1 / (1 + targetMaxWeaponAttackResistance);
                double bonusAttackCoefficient = bonusAttackResistance * BonusAttackCoefficient(player);
                double minDamage = weaponAttackWeakness * Values[BasicAttribute.MinWeaponAtk].Total + bonusAttackCoefficient * bonusAttack;
                double maxDamage = weaponAttackWeakness * Values[BasicAttribute.MaxWeaponAtk].Total + bonusAttackCoefficient * bonusAttack;
                return (minDamage, maxDamage);
            case FieldNpc npc:
                return (1, 1);
        }
        return (1, 1);

        double BonusAttackCoefficient(FieldPlayer player) {
            int rightHandRarity = player.Session.Item.Equips.Get(EquipSlot.RH)?.Rarity ?? 0;
            int leftHandRarity = player.Session.Item.Equips.Get(EquipSlot.LH)?.Rarity ?? 0;
            return BonusAttack.Coefficient(rightHandRarity, leftHandRarity, player.Value.Character.Job.Code());
        }
    }

    /// <summary>
    /// Get actor's critical damage result. Takes into consideration the target's critical damage resistance.
    /// </summary>
    /// <param name="targetCriticalDamageResistance">Target's critical damage resistance (a rate: 0.2 cuts the bonus by a fifth)</param>
    /// <returns>Critical damage</returns>
    public float GetCriticalDamage(float targetCriticalDamageResistance) {
        float criticalDamage = Actor switch {
            FieldPlayer or FieldNpc => Lua.CalcCritDamage(Values[BasicAttribute.CriticalDamage].Total, targetCriticalDamageResistance, Values[SpecialAttribute.CriticalDamageIgnoreLimit].Total),
            _ => 0,
        };
        return criticalDamage;
    }

    /// <summary>
    /// Gets the critical rate of the actor - uses a different formula for players and NPCs.
    /// </summary>
    /// <param name="targetCriticalEvasion"></param>
    /// <param name="skillId">Source skill ID</param>
    /// <returns>DamageType. If successful crit, returns DamageType.Critical. else returns DamageType.Normal</returns>
    public DamageType GetCriticalRate(long targetCriticalEvasion = 0, int skillId = 0) {
        return Random.Shared.NextDouble() < CriticalChance(targetCriticalEvasion, skillId) ? DamageType.Critical : DamageType.Normal;
    }

    // The chance GetCriticalRate rolls against.
    public double CriticalChance(long targetCriticalEvasion = 0, int skillId = 0) {
        double casterCriticalOverride = 0;
        float criticalChance = 0;
        switch (Actor) {
            case FieldPlayer player:
                criticalChance = Lua.CalcPlayerCritRate((int) player.Value.Character.Job.Code(), player.Stats.Values[BasicAttribute.Luck].Total, player.Stats.Values[BasicAttribute.CriticalRate].Total, targetCriticalEvasion,
                    player.Stats.Values[SpecialAttribute.CriticalRateIgnoreLimit].Total, player.Stats.Values[SpecialAttribute.CriticalRateIgnoreLimitExtra].Total);
                casterCriticalOverride = player.Buffs.TotalCompulsionRate(BuffCompulsionEventType.CritChanceOverride, skillId);
                break;
            case FieldNpc npc:
                criticalChance = Lua.CalcNpcCritRate(npc.Stats.Values[BasicAttribute.Luck].Total, npc.Stats.Values[BasicAttribute.CriticalRate].Total, targetCriticalEvasion);
                break;
            default:
                break;
        }

        return Math.Max(criticalChance, casterCriticalOverride);
    }

    // The level stats of |job|: the cached table, or the job's own one when the job changed since it was picked.
    internal IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>> LevelStats(JobCode job) {
        if (job != levelStatsJob && jobStats.TryGetValue(job, out IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>? stats)) {
            levelStats = stats;
            levelStatsJob = job;
        }
        return levelStats;
    }

    /// <param name="levelUp">A level change: KMS2 fills HP and SP on a level up (one Stat packet with both after it).</param>
    public void Refresh(bool levelUp = false) {
        if (Actor is FieldNpc npc) {
            RefreshNpc(npc);
            return;
        }
        if (Actor is not FieldPlayer player) {
            return;
        }

        // Do not refresh stats if player is mounted
        if (player.Session.Ride.RideType is RideOnType.Battle or RideOnType.Object) {
            return;
        }

        // Before the totals are rebuilt: giving or taking an effect that carries stats refreshes on its own.
        List<SetBonusMetadata> setBonuses = RefreshSetEffects(player);
        TakeUncountedItemEffects(player);

        Character character = player.Value.Character;
        if (LevelStats(character.Job.Code()).TryGetValue(character.Level, out IReadOnlyDictionary<BasicAttribute, long>? metadata)) {
            Values.Reset(metadata, character.Job.Code());
        } else {
            Log.Logger.Error("Failed to refresh stats for {Job} level {Level}.", character.Job.Code(), character.Level);
            Values.Reset(character.Job.Code(), character.Level);
        }

        // Spent attribute points come after the base and before the items, on every rebuild.
        player.Session.Config.AddStatAllocation(Values);
        AddEquips(player);
        AddPet(player);
        AddSetBonuses(setBonuses);
        AddBuffs(player);
        AddHotTime(player.Session);
        Values.AddDerivedTotal(character.Job.Code());
        Values.Total();
        StatConversion(player);

        // Current was held through the rebuild; it now keeps its share of the new Total, and a dead player stays at 0 HP until revived.
        Values.EndReset(player.IsDead, levelUp);

        player.Session.Send(StatsPacket.Init(player));
        Actor.Field.Broadcast(StatsPacket.Update(player), player.Session);

        player.Session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = player.Session.AccountId,
            CharacterId = player.Session.CharacterId,
            Health = new HealthUpdate {
                CurrentHp = Values[BasicAttribute.Health].Current,
                TotalHp = Values[BasicAttribute.Health].Total,
            },
            Async = true,
        });
    }

    private int sentGearScore = -1;

    // The gear score goes to the world when it changes, so the guild, club and party lists of the other players
    // follow it. It was never sent: those lists showed 0.
    public void SendGearScore(GameSession session, int gearScore) {
        if (gearScore == sentGearScore) {
            return;
        }
        sentGearScore = gearScore;
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            GearScore = gearScore,
            Async = true,
        });
    }

    public void ResetActor(IActor actor) {
        Actor = actor;
    }

    private static int penaltyDivWarned;

    // An item the client does not count gives it no stats either (SetItemRules.GivesStats).
    private static Func<Item, int, bool> Gives(FieldPlayer player, long now) {
        bool vip = player.Session.Config.IsPremiumClubActive();
        long characterId = player.Value.Character.Id;
        int characterLevel = player.Value.Character.Level;
        return (item, layer) => SetItemRules.GivesStats(layer, SetItemRules.Counted(SetItemRules.Expired(item.ExpiryTime, now, item.Pet != null),
            item.Metadata.Limit.RequireVip, vip, item.Transfer?.Binding?.CharacterId ?? 0, characterId, item.Metadata.Limit.Level, characterLevel));
    }

    // The effects of the worn gear and its gems (AddItemBuffs) leave with the stats: a worn item that runs out (or
    // otherwise stops counting) keeps its place, but the client rebuilds its abilities from the items it can use only
    // (equipment.cpp:2950 marks it, :3021 re-adds CanUseItem items), so its effects are taken here as an unequip
    // takes them. An effect another counted item gives too stays. Before the totals are rebuilt, as the set effects.
    private static void TakeUncountedItemEffects(FieldPlayer player) {
        Func<Item, int, bool> gives = Gives(player, DateTime.Now.ToEpochSeconds());
        ILookup<bool, Item> gear = player.Session.Item.Equips.Gear.Values.ToLookup(item => gives(item, 0));
        int[] taken = BuffManager.ItemEffectsToTake(gear[false].SelectMany(player.Buffs.ItemEffectIds),
            gear[true].SelectMany(player.Buffs.ItemEffectIds), id => player.Buffs.HasBuff(id));
        if (taken.Length > 0) {
            player.Buffs.Remove(taken.Select(id => (id, player.ObjectId)).ToArray());
        }
    }

    private void AddEquips(FieldPlayer player) {
        Values.GearScore = 0;
        long now = DateTime.Now.ToEpochSeconds();
        expiredWorn = ExpiredWorn(player, now);
        bool pvp = player.Field.Metadata.Property.Type == MapType.Pvp;
        Func<Item, int, bool> gives = Gives(player, now);

        // The outfit tab's items add their own stats as the equip tab's do (equipment.cpp:3009-3020 runs for
        // every layer but the second outfit layer).
        foreach (Item item in player.Session.Item.Equips.Outfit.Values) {
            if (item.Stats != null && gives(item, 1)) {
                AddItemStats(item.Stats);
            }
        }

        var worn = new List<GearScoreRules.Worn>();
        foreach (Item item in player.Session.Item.Equips.Gear.Values) {
            if (!gives(item, 0)) {
                continue;
            }
            if (item.Stats != null) {
                AddItemStats(item.Stats);
            }

            if (pvp) {
                AddPvpUpgrades(item);
            }

            // Add enhancement attributes to character attributes
            if (!pvp && item.Enchant is not null) {
                foreach (KeyValuePair<BasicAttribute, BasicOption> kvp in item.Enchant.BasicOptions) {
                    AddUpgradeOption(item, kvp.Key, kvp.Value);
                }
            }

            if (!pvp && item.LimitBreak is not null) {
                foreach (KeyValuePair<BasicAttribute, BasicOption> kvp in item.LimitBreak.BasicOptions) {
                    AddUpgradeOption(item, kvp.Key, kvp.Value);
                }
            }
            if (item.LimitBreak is not null) {
                foreach (KeyValuePair<SpecialAttribute, SpecialOption> kvp in item.LimitBreak.SpecialOptions) {
                    Values[kvp.Key].AddTotal(SetItemRules.LimitBreakSpecial(pvp, kvp.Value.Value));
                }
            }

            if (item.Socket != null) {
                for (int index = 0; index < item.Socket.UnlockSlots; index++) {
                    ItemGemstone? gem = item.Socket.Sockets[index];
                    if (gem != null && gem.Stats != null) {
                        AddItemStats(gem.Stats);
                    }
                }
            }

            player.Buffs.AddItemBuffs(item);

            // The gear score, as the client computes it (GearScoreRules).
            int dataRank = item.Metadata.Property.Rank ?? 0;
            // The client's item data subtype (GearScoreRules.SubType), not the server data's.
            int subType = GearScoreRules.SubType(item.Id);
            (int level, int enchantLevel) = item.Metadata.Property.Type == 1
                ? GearScoreRules.ItemLevel(item.Metadata.Property.GearScore, dataRank, item.Rarity, subType, item.Enchant?.Enchants ?? 0, item.LimitBreak?.Level ?? 0)
                : (0, 0);
            EquipSlot slot = item.EquipSlot();
            worn.Add(new GearScoreRules.Worn(slot == EquipSlot.RH ? 1 : slot == EquipSlot.LH ? 2 : 0, GearScoreRules.IsPaired(subType), level, enchantLevel));
        }

        float penaltyDiv = player.Session.ServerTableMetadata.ConstantsTable.BothHandGearScorePenaltyDiv;
        if (penaltyDiv <= 0) {
            // No row in the constants: the client's own default.
            if (Interlocked.Exchange(ref penaltyDivWarned, 1) == 0) {
                Log.Logger.Warning("The constants have no BothHandGearScorePenaltyDiv; the gear score uses the client's default {Default}", GearScoreRules.DEFAULT_PENALTY_DIV);
            }
            penaltyDiv = GearScoreRules.DEFAULT_PENALTY_DIV;
        }
        Values.GearScore = GearScoreRules.Total(worn, penaltyDiv);
        Log.Logger.Debug("Final Gearscore for {name} ({id}): {gearscore}", player.Value.Character.Name, player.Value.Character.Id, Values.GearScore);
        player.Session.Dungeon.UpdateDungeonEnterLimit();
        player.Session.ConditionUpdate(ConditionType.item_gear_score, counter: Values.GearScore);
        SendGearScore(player.Session, Values.GearScore);
    }

    // The set-bonus parts that hold for the worn gear and the worn outfit (SetItemRules), and their effects:
    // the missing ones are given, and the ones given earlier that no longer hold are taken. These effects
    // have no duration, so the logout save drops them; this runs at login and on every equip change.
    private List<SetBonusMetadata> RefreshSetEffects(FieldPlayer player) {
        long now = DateTime.Now.ToEpochSeconds();
        bool vip = player.Session.Config.IsPremiumClubActive();
        long characterId = player.Value.Character.Id;
        int characterLevel = player.Value.Character.Level;
        // Only the items the client counts (SetItemRules.Counted).
        int[][] Pieces(IEnumerable<Item> worn) {
            return worn.Where(item => SetItemRules.Counted(SetItemRules.Expired(item.ExpiryTime, now, item.Pet != null),
                    item.Metadata.Limit.RequireVip, vip, item.Transfer?.Binding?.CharacterId ?? 0, characterId, item.Metadata.Limit.Level, characterLevel))
                .Select(item => item.Metadata.Property.SetOptionIds).ToArray();
        }
        List<SetBonusMetadata> bonuses = SetItemRules.Bonuses([
            Pieces(player.Session.Item.Equips.Gear.Values),
            Pieces(player.Session.Item.Equips.Outfit.Values),
        ], player.Session.TableMetadata.SetItemTable.Entries);

        List<(int Id, short Level)> effects = SetItemRules.Effects(bonuses);
        (int Id, int CasterId)[] stale = setEffectIds.Where(id => effects.All(effect => effect.Id != id))
            .Select(id => (id, player.ObjectId)).ToArray();
        setEffectIds.Clear();
        foreach ((int id, short _) in effects) {
            setEffectIds.Add(id);
        }
        if (stale.Length > 0) {
            player.Buffs.Remove(stale);
        }
        foreach ((int id, short level) in effects) {
            if (!player.Buffs.HasBuff(id, level) && player.Field.SkillMetadata.TryGetEffect(id, level, out _)) {
                player.Buffs.AddBuff(player, player, id, level, player.Field.FieldTick);
            }
        }
        return bonuses;
    }

    // Set-bonus stats, taken the way a buff's are (AddBuffs).
    private void AddSetBonuses(List<SetBonusMetadata> bonuses) {
        foreach (SetBonusMetadata bonus in bonuses) {
            foreach ((BasicAttribute attribute, long value) in bonus.Values) {
                Values[attribute].AddTotal(value);
            }
            foreach ((BasicAttribute attribute, float rate) in bonus.Rates) {
                Values[attribute].AddRate(rate);
            }
            foreach ((SpecialAttribute attribute, float value) in bonus.SpecialValues) {
                Values[attribute].AddTotal((long) value);
            }
            foreach ((SpecialAttribute attribute, float rate) in bonus.SpecialRates) {
                Values[attribute].AddTotal(new SpecialOption(rate));
            }
        }
    }

    // A buff's status values and rates count once per stack, as the client sums them (Buff.StackFactor).
    // The running SAHotTime events' bonus, for every player: the client adds the same sums to its own special
    // attribute totals (CEquipment::GetAbilityTotal, equipment.cpp:740).
    internal void AddHotTime(GameSession session) {
        Dictionary<SpecialAttribute, long> bonus = HotTimeBonus(session);
        foreach ((SpecialAttribute attribute, long value) in bonus) {
            Values[attribute].AddTotal(value);
        }
        hotTime = bonus;
    }

    private Dictionary<SpecialAttribute, long> hotTime = new();

    private static Dictionary<SpecialAttribute, long> HotTimeBonus(GameSession session) {
        return EventSaleRules.HotTimeBonus(session.FindEvent(GameEventType.SAHotTime)
            .Select(gameEvent => gameEvent.Metadata.Data).OfType<SAHotTime>());
    }

    // The worn items past their time when the totals were built.
    private int expiredWorn;

    private static int ExpiredWorn(FieldPlayer player, long now) {
        var equips = player.Session.Item.Equips;
        return ExpiredWorn(equips.Gear.Values.Concat(equips.Outfit.Values).Concat(equips.Badge.Values), now);
    }

    internal static int ExpiredWorn(IEnumerable<Item> worn, long now) {
        return worn.Count(item => SetItemRules.Expired(item.ExpiryTime, now, item.Pet != null));
    }

    // A worn item that runs out stays worn. The client drops its look, stats and set count by itself at that
    // moment (CEquipment tick, equipment.cpp:2950) with no packet; the totals here follow it.
    public void RefreshExpiry() {
        if (Actor is not FieldPlayer player) {
            return;
        }
        if (ExpiryChanged(expiredWorn, ExpiredWorn(player, DateTime.Now.ToEpochSeconds()))) {
            Refresh();
        }
    }

    // The tick rebuilds when the count of worn items past their time is not the one the totals were built with.
    internal static bool ExpiryChanged(int built, int expiredNow) => built != expiredNow;

    // An event that starts or stops (its dates, its days of the week, its part of the day) changes the bonus
    // without anything else rebuilding the stats: rebuild when the sums are not the ones the totals hold.
    public void RefreshHotTime() {
        if (Actor is not FieldPlayer player) {
            return;
        }
        Dictionary<SpecialAttribute, long> bonus = HotTimeBonus(player.Session);
        if (bonus.Count != hotTime.Count || bonus.Any(entry => hotTime.GetValueOrDefault(entry.Key) != entry.Value)) {
            Refresh();
        }
    }

    // An npc's totals are its own stats with the status of the buffs it holds, counted as a player's are (values,
    // rates and special values times the stack count): a job skill's debuff on a monster (10100181 defense -30 %,
    // 10200061 attack -9 %, 11100242 evasion -3 and critical evasion -20) changes the stats the damage of a hit reads.
    // The client keeps no stats of an npc but its health (StatsPacket), so the server chooses this; before, an npc's
    // stats never took a buff. A change of its maximum health goes to the field, with the health kept at its share.
    private void RefreshNpc(FieldNpc npc) {
        if (npcStats == null) {
            return;
        }
        if (RebuildNpc(Values, npcStats, npc.Buffs.EnumerateBuffs(), npc.IsDead)) {
            npc.Field.Broadcast(StatsPacket.Update(npc, BasicAttribute.Health));
        }
    }

    // Rebuilds an npc's totals from its own stats and the buffs it holds; true when its maximum health changed, which
    // the field is told of.
    public static bool RebuildNpc(Stats values, IReadOnlyDictionary<BasicAttribute, long> npcStats, IEnumerable<Buff> buffs, bool dead) {
        long health = values[BasicAttribute.Health].Total;
        values.Reset(npcStats);
        AddBuffs(values, buffs);
        values.Total();
        FloorNpc(values);
        values.EndReset(dead);
        return values[BasicAttribute.Health].Total != health;
    }

    // A debuff never takes an npc's maximum health or movement speed under 1: rates that sum to -1 or less (the data
    // holds MovementSpeed -1 and -0.5 that stacks to 3) would leave a walk with speed 0 that never arrives, and a
    // maximum health of 0 would kill the npc with no killer, then refill it to full when the buff ends. An npc with no
    // speed of its own (Base 0) keeps 0.
    public static void FloorNpc(Stats values) {
        foreach (BasicAttribute attribute in new[] { BasicAttribute.Health, BasicAttribute.MovementSpeed }) {
            Stat stat = values[attribute];
            if (stat.Base >= 1 && stat.Total < 1) {
                stat.AddTotal(1 - stat.Total);
            }
        }
    }

    private void AddBuffs(IActor actor) => AddBuffs(Values, actor.Buffs.EnumerateBuffs());

    // The status of the enabled buffs held, and the invoke damage bonus among them. A buff whose begin condition
    // fails (Buff.Enabled false: an HP band, a weapon, a buff it needs) counts for nothing, as in the client's sums
    // (AdditionalCliList::FUN_140972a00 and CollectInvokeEffectStats skip a buff without m_bEnabled); before, its
    // stats were counted the whole time it was held.
    public static void AddBuffs(Stats values, IEnumerable<Buff> buffs) {
        List<Buff> held = buffs.Where(buff => buff.Enabled).ToList();
        // A suspended buff (its holder on a battle mount) gives no status; its invoke bonus still counts.
        foreach (Buff buff in held.Where(buff => !buff.Suspended)) {
            AddStatus(values, buff.Metadata.Status, buff.Stacks);
        }
        foreach ((SpecialAttribute attribute, float rate) in InvokeDamageBonus(
                     held.Select(buff => (buff.Metadata.InvokeEffect, buff.Stacks)),
                     held.Select(buff => (buff.Id, buff.Metadata.Property.InvokeGroupId)))) {
            values[attribute].AddTotal(new SpecialOption(rate));
        }
        foreach (Buff buff in held) {
            BuffManager? casterBuffs = buff.Caster?.Buffs;
            if (casterBuffs == null) {
                continue;
            }
            foreach ((BasicAttribute attribute, long value) in InvokeDebuffBonus(type =>
                         casterBuffs.GetEffectInvokeValues(type, buff.Id, buff.Metadata.Property.InvokeGroupId).Item1)) {
                values[attribute].AddTotal(value);
            }
        }
    }

    // Invoke types 23 and 26 add a flat value to the evasion or critical evasion of the effect they name: the passive
    // 11100252 (levels 1-12) gives its debuff 11100242 a further -N evasion and -10N
    // critical evasion at level N. The client's mapping of these types (specialability.cpp FUN_14081d980) has no
    // caller, and it counts them for display only, so the server chooses: the bonus is on the holder of the named
    // effect, from the invokes its caster holds (as IncreaseDotDamage is), times the invoke's stack count.
    public static List<(BasicAttribute Attribute, long Value)> InvokeDebuffBonus(Func<InvokeEffectType, int> casterValue) {
        List<(BasicAttribute, long)> bonus = [];
        foreach ((InvokeEffectType type, BasicAttribute attribute) in DebuffInvokes) {
            int value = casterValue(type);
            if (value != 0) {
                bonus.Add((attribute, value));
            }
        }
        return bonus;
    }

    private static readonly (InvokeEffectType Type, BasicAttribute Attribute)[] DebuffInvokes = [
        (InvokeEffectType.IncreaseEvasionDebuff, BasicAttribute.Evasion),
        (InvokeEffectType.IncreaseCritEvasionDebuff, BasicAttribute.CriticalEvasion),
    ];

    // A buff's status in a rebuild, once per stack: its values and special values to the totals, its rates to the
    // rates Stats.Total applies, its special rates in thousandths (an item's special rate is kept so).
    public static void AddStatus(Stats values, AdditionalEffectMetadataStatus status, int stacks) {
        foreach ((BasicAttribute attribute, long value) in status.Values) {
            values[attribute].AddTotal(Buff.ByStacks(value, stacks));
        }
        foreach ((BasicAttribute attribute, float rate) in status.Rates) {
            values[attribute].AddRate(Buff.ByStacks(rate, stacks));
        }
        foreach ((SpecialAttribute attribute, float value) in status.SpecialValues) {
            values[attribute].AddTotal((long) Buff.ByStacks(value, stacks));
        }
        foreach ((SpecialAttribute attribute, float rate) in status.SpecialRates) {
            values[attribute].AddTotal(new SpecialOption(Buff.ByStacks(rate, stacks)));
        }
    }

    // Invoke types 38 and 40 add to the offensive physical or magical damage of the effect they name, in thousandths
    // times the invoke's stack count: 90050806 (up to 5 stacks) adds 20 to 90050805's physical damage of 2 %, and
    // 90050827 adds 52 to 90050825's magical damage of 2.6 %. The client counts these for no stat of its own (CollectInvokeEffectStats feeds
    // only skill cooldowns, spirit and the DPS meter), so the server chooses: the bonus is the holder's while it holds
    // the named effect too (each invoke buff begins on hasBuffID of it), once however many of it are held.
    public static List<(SpecialAttribute Attribute, float Rate)> InvokeDamageBonus(
            IEnumerable<(AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)> invokes, IEnumerable<(int Id, int GroupId)> held) {
        List<(int Id, int GroupId)> effects = held.ToList();
        List<(SpecialAttribute, float)> bonus = [];
        foreach ((AdditionalEffectMetadataInvokeEffect? invoke, int stacks) in invokes) {
            if (invoke == null || !effects.Any(effect => BuffManager.EffectKeyed(invoke, effect.Id, effect.GroupId))) {
                continue;
            }
            for (int i = 0; i < invoke.Types.Length; i++) {
                SpecialAttribute? attribute = invoke.Types[i] switch {
                    InvokeEffectType.IncreaseOffensivePhysicalDamage => SpecialAttribute.OffensivePhysicalDamage,
                    InvokeEffectType.IncreaseOffensiveMagicalDamage => SpecialAttribute.OffensiveMagicalDamage,
                    _ => null,
                };
                if (attribute != null && invoke.Values[i] != 0) {
                    bonus.Add((attribute.Value, Buff.ByStacks(invoke.Values[i] / 1000f, stacks)));
                }
            }
        }
        return bonus;
    }

    private void StatConversion(FieldPlayer player) {
        foreach (Buff buff in player.Buffs.EnumerateBuffs().Where(buff => buff.Counted)) {
            if (buff.Metadata.Status.Conversion != null) {
                float sum = Values[buff.Metadata.Status.Conversion.BaseAttribute].Total;
                sum *= buff.Metadata.Status.Conversion.Rate;
                Values[buff.Metadata.Status.Conversion.ResultAttribute].AddTotal((long) sum);
            }
        }
    }

    // An enchant or limit break option of a ratio stat is a rate in thousandths of the item's constant
    // option of the same attribute; of any other stat it is a flat value (CItemStatus::GetStatValue).
    private void AddUpgradeOption(Item item, BasicAttribute attribute, BasicOption option) {
        if (!BasicOption.IsRatio(attribute)) {
            Values[attribute].AddTotal(option);
            return;
        }
        Values[attribute].AddTotal(RatioUpgrade(item, attribute, option));
    }

    private static long RatioUpgrade(Item item, BasicAttribute attribute, BasicOption option) {
        if (item.Stats != null && item.Stats[ItemStats.Type.Constant].Basic.TryGetValue(attribute, out BasicOption constant)) {
            return (long) (constant.Value * (option.Value / 1000.0));
        }
        return 0;
    }

    // In a PvP field the client counts an item's enchant and limit break for the ratio stats only
    // (SetItemRules.PvpUpgrade); the limit break's special options are not counted there either
    // (SetItemRules.LimitBreakSpecial).
    private void AddPvpUpgrades(Item item) {
        foreach (BasicAttribute attribute in new[] { BasicAttribute.Defense, BasicAttribute.MinWeaponAtk, BasicAttribute.MaxWeaponAtk }) {
            long enchant = item.Enchant != null && item.Enchant.BasicOptions.TryGetValue(attribute, out BasicOption enchantOption)
                ? RatioUpgrade(item, attribute, enchantOption) : 0;
            long limitBreak = item.LimitBreak != null && item.LimitBreak.BasicOptions.TryGetValue(attribute, out BasicOption limitOption)
                ? RatioUpgrade(item, attribute, limitOption) : 0;
            Values[attribute].AddTotal(SetItemRules.PvpUpgrade(BasicOption.IsRatio(attribute), attribute == BasicAttribute.Defense, enchant, limitBreak));
        }
    }

    // The summoned pet's item: its stats count as a worn item's do, and its constant values grow with the
    // pet's level (PetRules.LevelBonus). The client adds the same to its own totals
    // (CEquipment::FUN_1411e76c0, equipment.cpp:3031-3048).
    private void AddPet(FieldPlayer player) {
        Item? pet = player.Session.Pet?.Pet;
        if (pet?.Stats == null || pet.Pet == null) {
            return;
        }

        AddItemStats(pet.Stats);
        if (!player.Session.ItemMetadata.TryGetPet(pet.Metadata.Property.PetId, out PetMetadata? metadata)) {
            return;
        }

        ItemStats.Option constant = pet.Stats[ItemStats.Type.Constant];
        foreach ((BasicAttribute attribute, BasicOption option) in constant.Basic) {
            Values[attribute].AddTotal(new BasicOption(PetRules.LevelBonus(option.Value, metadata.OptionFactor, pet.Pet.Level)));
        }
        foreach ((SpecialAttribute attribute, SpecialOption option) in constant.Special) {
            Values[attribute].AddTotal(new SpecialOption(PetRules.LevelBonus(option.Value, metadata.OptionFactor, pet.Pet.Level)));
        }
    }

    private void AddItemStats(ItemStats stats) {
        for (int type = 0; type < ItemStats.TYPE_COUNT; type++) {
            foreach ((BasicAttribute attribute, BasicOption option) in stats[(ItemStats.Type) type].Basic) {
                Values[attribute].AddTotal(option);
            }
            foreach ((SpecialAttribute attribute, SpecialOption option) in stats[(ItemStats.Type) type].Special) {
                Values[attribute].AddTotal(option);
            }
        }
    }
}

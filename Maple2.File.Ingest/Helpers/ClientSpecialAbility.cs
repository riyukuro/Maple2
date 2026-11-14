using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The server's stats of the client's SpecialAbility rows. An additional effect level of the client
// (additional/<nnn>.xml) lists its stats as <SpecialAbility><v type="<option>" value="<int>"/></SpecialAbility>; the
// client keeps one value per type, the last nonzero one (AdditionalData::LoadLevel, additionalactionadditional.cpp:1227).
// The option names and value types come from table/specialabilitystringtable.xml (LoadSpecialAbilityStringTable,
// commonfiles/specialability.cpp:170): valueType 1 is a flat value, valueType 2 a rate in thousandths, which the
// server keeps as a fraction (the data pack's penrate 0.08 is the client's pen 80, firedamagerate 0.01 its fireDamage
// 10, hprate 11.5 its hpRate 11500).
// Where a type goes on the server:
// - the basic stats (hp, msp, pap, ...) and their xRate types: the BasicAttribute value or rate;
// - wapRate: the MinWeaponAtk and MaxWeaponAtk rates;
// - improve_stat_wap_u: the MaxWeaponAtk rate (the client shows it as a max weapon attack percent; the data
//   pack writes the same rows as Rates.MaxWeaponAtk, 90050836). Its SpecialAttribute (MaxWeaponAttack) is read by
//   no server calc;
// - finalAttackPower: the PhysicalAtk and MagicalAtk rates. Chosen here: the server has no final attack stat of its
//   own; the data pack writes the same rows as finalpaprate and finalmaprate (70002591, 90000764);
// - offensive_physicaldamage and offensive_magicaldamage: the OffensivePhysicalDamage and OffensiveMagicalDamage
//   special rates (the data pack's OffensiveProperty papDamageR 1.5 is the client's 1500);
// - defensive_physicaldamage and defensive_magicaldamage: the physical and magical damage taken rates (the data
//   pack's DefensiveProperty papDamageR 0.3 is the client's 300, 90001005);
// - every other type whose name is a SpecialAttribute option of the parser (MapperExtensions.OptionIndex): that
//   special value (valueType 1) or rate (valueType 2);
// - atpToBap and strToInt (valueType 2, shown as bonus attack from accuracy and intelligence from strength):
//   the stat conversion, Accuracy to BonusAtk and Strength to Intelligence at the rate (the data pack
//   writes the same rows as <Status statChangeBase statChangeResult statChangeRate>, 2.2 on 90051195 and 0.7 on
//   10900041, and the client has no statChange attributes). A level holds one conversion; a second one is unmapped.
// - cap_ignore_limit, cad_ignore_limit and cap_ignore_limit_extra (ids 202, 203, 205): the special stats of the same
//   ids, which the client adds after the clamps of its crit rate and crit damage functions;
// The rest (defensive_damageV, aevp, reduce_do_heal_hp, ...) has no server stat; Add returns false and counts it in
// Unmapped.
public sealed class ClientSpecialAbility {
    // The stats an additional effect level adds, as AdditionalEffectMetadataStatus holds them.
    public sealed class Stats {
        public Dictionary<BasicAttribute, long> Values { get; init; } = new();
        public Dictionary<BasicAttribute, float> Rates { get; init; } = new();
        public Dictionary<SpecialAttribute, float> SpecialValues { get; init; } = new();
        public Dictionary<SpecialAttribute, float> SpecialRates { get; init; } = new();
        public float PhysicalDamageTakenRate { get; set; }
        public float MagicalDamageTakenRate { get; set; }
        public AdditionalEffectMetadataStatus.StatConversion? Conversion { get; set; }
    }

    public const string TableName = "table/specialabilitystringtable.xml";

    // The parser's SpecialAbility option names in its index order (AdditionalEffect StatusProperty.SpecialAbility
    // Value(i) and Rate(i)), the order MapperExtensions.OptionIndex numbers.
    private static readonly string[] ParserOptions = [
        "seg", "smd", "sss", "dashdistance", "spd", "sid", "finaladditionaldamage", "cri", "sgi", "sgi_leader", "sgi_elite", "sgi_boss",
        "killhprestore", "killsprestore", "killeprestore", "heal", "receivedhealincrease", "icedamage", "firedamage", "darkdamage", "lightdamage",
        "poisondamage", "thunderdamage", "nddincrease", "lddincrease", "parpen", "marpen", "icedamagereduce", "firedamagereduce", "darkdamagereduce",
        "lightdamagereduce", "poisondamagereduce", "thunderdamagereduce", "stunreduce", "conditionreduce", "skillcooldown",
        "neardistancedamagereduce", "longdistancedamagereduce", "knockbackreduce", "stunprocndd", "stunprocldd", "knockbackprocndd",
        "knockbackprocldd", "snareprocndd", "snareprocldd", "aoeprocndd", "aoeprocldd", "npckilldropitemincrate", "seg_questreward",
        "smd_questreward", "seg_fishingreward", "seg_arcadereward", "seg_playinstrumentreward", "invoke_effect1", "invoke_effect2", "invoke_effect3",
        "pvpdamageincrease", "pvpdamagereduce", "improveguildexp", "improveguildcoin", "improvemassiveeventbexpball", "reduce_meso_trade_fee",
        "reduce_enchant_matrial_fee", "reduce_merat_revival_fee", "improve_mining_reward_item", "improve_breeding_reward_item",
        "improve_blacksmithing_reward_mastery", "improve_engraving_reward_mastery", "improve_gathering_reward_item", "improve_farming_reward_item",
        "improve_alchemist_reward_mastery", "improve_cooking_reward_mastery", "improve_acquire_gathering_exp", "skill_levelup_tier_1",
        "skill_levelup_tier_2", "skill_levelup_tier_3", "skill_levelup_tier_4", "skill_levelup_tier_5", "skill_levelup_tier_6",
        "skill_levelup_tier_7", "skill_levelup_tier_8", "skill_levelup_tier_9", "skill_levelup_tier_10", "skill_levelup_tier_11",
        "skill_levelup_tier_12", "skill_levelup_tier_13", "skill_levelup_tier_14", "improve_massive_ox_exp", "improve_massive_trapmaster_exp",
        "improve_massive_finalsurvival_exp", "improve_massive_crazyrunner_exp", "improve_massive_sh_crazyrunner_exp", "improve_massive_escape_exp",
        "improve_massive_springbeach_exp", "improve_massive_dancedance_exp", "improve_massive_ox_msp", "improve_massive_trapmaster_msp",
        "improve_massive_finalsurvival_msp", "improve_massive_crazyrunner_msp", "improve_massive_sh_crazyrunner_msp", "improve_massive_escape_msp",
        "improve_massive_springbeach_msp", "improve_massive_dancedance_msp", "npc_hit_reward_sp_ball", "npc_hit_reward_ep_ball",
        "improve_honor_token", "improve_pvp_exp", "improve_darkstream_damage", "reduce_darkstream_recive_damage", "improve_darkstream_evp",
        "fishing_double_mastery", "playinstrument_double_mastery", "complete_fieldmission_msp", "improve_glide_vertical_velocity",
        "additionaleffect_95000018", "additionaleffect_95000012", "additionaleffect_95000014", "additionaleffect_95000020",
        "additionaleffect_95000021", "additionaleffect_95000022", "additionaleffect_95000023", "additionaleffect_95000024",
        "additionaleffect_95000025", "additionaleffect_95000026", "additionaleffect_95000027", "additionaleffect_95000028",
        "additionaleffect_95000029", "reduce_recovery_ep_inv", "improve_stat_wap_u", "mining_double_reward", "breeding_double_reward",
        "gathering_double_reward", "farming_double_reward", "blacksmithing_double_reward", "engraving_double_reward", "alchemist_double_reward",
        "cooking_double_reward", "mining_double_mastery", "breeding_double_mastery", "gathering_double_mastery", "farming_double_mastery",
        "blacksmithing_double_mastery", "engraving_double_mastery", "alchemist_double_mastery", "cooking_double_mastery", "improve_chaosraid_wap",
        "improve_chaosraid_asp", "improve_chaosraid_atp", "improve_chaosraid_hp", "improve_recovery_ball", "improve_fieldboss_kill_exp",
        "improve_fieldboss_kill_drop", "reduce_fieldboss_recive_damage", "additionaleffect_95000016", "improve_pettrap_reward", "mining_multiaction",
        "breeding_multiaction", "gathering_multiaction", "farming_multiaction", "reduce_damage_by_targetmaxhp", "reduce_meso_revival_fee",
        "improve_riding_run_speed", "improve_dungeon_reward_meso", "improve_shop_buying_meso", "improve_itembox_reward_meso",
        "reduce_remakeoption_fee", "reduce_airtaxi_fee", "improve_socket_unlock_probability", "reduce_gemstone_upgrade_fee",
        "reduce_pet_remakeoption_fee", "improve_riding_speed", "improve_survival_kill_exp", "improve_survival_time_exp", "offensive_physicaldamage",
        "offensive_magicaldamage", "reduce_gameitem_socket_unlock_fee"
    ];

    // The basic stats by the client's type name. Flat ones are valueType 1, rates (and abp, pen) valueType 2.
    private static readonly Dictionary<string, BasicAttribute[]> Basic = new(StringComparer.OrdinalIgnoreCase) {
        ["str"] = [BasicAttribute.Strength],
        ["dex"] = [BasicAttribute.Dexterity],
        ["int"] = [BasicAttribute.Intelligence],
        ["luk"] = [BasicAttribute.Luck],
        ["hp"] = [BasicAttribute.Health],
        ["hp_rgp"] = [BasicAttribute.HpRegen],
        ["sp"] = [BasicAttribute.Spirit],
        ["sp_rgp"] = [BasicAttribute.SpRegen],
        ["ep"] = [BasicAttribute.Stamina],
        ["ep_inv"] = [BasicAttribute.StaminaRegenInterval],
        ["asp"] = [BasicAttribute.AttackSpeed],
        ["msp"] = [BasicAttribute.MovementSpeed],
        ["atp"] = [BasicAttribute.Accuracy],
        ["evp"] = [BasicAttribute.Evasion],
        ["cap"] = [BasicAttribute.CriticalRate],
        ["cad"] = [BasicAttribute.CriticalDamage],
        ["car"] = [BasicAttribute.CriticalEvasion],
        ["ndd"] = [BasicAttribute.Defense],
        ["abp"] = [BasicAttribute.PerfectGuard],
        ["pap"] = [BasicAttribute.PhysicalAtk],
        ["map"] = [BasicAttribute.MagicalAtk],
        ["par"] = [BasicAttribute.PhysicalRes],
        ["mar"] = [BasicAttribute.MagicalRes],
        ["pen"] = [BasicAttribute.Piercing],
        ["rmsp"] = [BasicAttribute.MountSpeed],
        ["bap"] = [BasicAttribute.BonusAtk],
        ["strRate"] = [BasicAttribute.Strength],
        ["dexRate"] = [BasicAttribute.Dexterity],
        ["intRate"] = [BasicAttribute.Intelligence],
        ["lukRate"] = [BasicAttribute.Luck],
        ["hpRate"] = [BasicAttribute.Health],
        ["hpInvRate"] = [BasicAttribute.HpRegenInterval],
        ["spRate"] = [BasicAttribute.Spirit],
        ["spInvRate"] = [BasicAttribute.SpRegenInterval],
        ["epRate"] = [BasicAttribute.Stamina],
        ["epInvRate"] = [BasicAttribute.StaminaRegenInterval],
        ["aspRate"] = [BasicAttribute.AttackSpeed],
        ["mspRate"] = [BasicAttribute.MovementSpeed],
        ["rmspRate"] = [BasicAttribute.MountSpeed],
        ["atpRate"] = [BasicAttribute.Accuracy],
        ["evpRate"] = [BasicAttribute.Evasion],
        ["capRate"] = [BasicAttribute.CriticalRate],
        ["carRate"] = [BasicAttribute.CriticalEvasion],
        ["nddRate"] = [BasicAttribute.Defense],
        ["jmpRate"] = [BasicAttribute.JumpHeight],
        ["papRate"] = [BasicAttribute.PhysicalAtk],
        ["mapRate"] = [BasicAttribute.MagicalAtk],
        ["parRate"] = [BasicAttribute.PhysicalRes],
        ["marRate"] = [BasicAttribute.MagicalRes],
        ["wapRate"] = [BasicAttribute.MinWeaponAtk, BasicAttribute.MaxWeaponAtk],
        ["improve_stat_wap_u"] = [BasicAttribute.MaxWeaponAtk],
        // Chosen here (see above).
        ["finalAttackPower"] = [BasicAttribute.PhysicalAtk, BasicAttribute.MagicalAtk],
    };

    // The conversion types: the attribute converted from and the one it adds to.
    private static readonly Dictionary<string, (BasicAttribute Base, BasicAttribute Result)> Conversion = new(StringComparer.OrdinalIgnoreCase) {
        ["atpToBap"] = (BasicAttribute.Accuracy, BasicAttribute.BonusAtk),
        ["strToInt"] = (BasicAttribute.Strength, BasicAttribute.Intelligence),
    };

    private static readonly Dictionary<string, SpecialAttribute> Special = BuildSpecial();

    private static Dictionary<string, SpecialAttribute> BuildSpecial() {
        var byIndex = new Dictionary<byte, SpecialAttribute>();
        foreach (SpecialAttribute attribute in Enum.GetValues<SpecialAttribute>()) {
            byte index = attribute.OptionIndex();
            if (index != byte.MaxValue) {
                byIndex.TryAdd(index, attribute);
            }
        }
        var special = new Dictionary<string, SpecialAttribute>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ParserOptions.Length; i++) {
            if (byIndex.TryGetValue((byte) i, out SpecialAttribute attribute)) {
                special.TryAdd(ParserOptions[i], attribute);
            }
        }
        special["offensive_physicaldamage"] = SpecialAttribute.OffensivePhysicalDamage;
        special["offensive_magicaldamage"] = SpecialAttribute.OffensiveMagicalDamage;
        special["cap_ignore_limit"] = SpecialAttribute.CriticalRateIgnoreLimit;
        special["cad_ignore_limit"] = SpecialAttribute.CriticalDamageIgnoreLimit;
        special["cap_ignore_limit_extra"] = SpecialAttribute.CriticalRateIgnoreLimitExtra;
        return special;
    }

    // valueType by option name.
    private readonly Dictionary<string, int> valueTypes = new(StringComparer.OrdinalIgnoreCase);

    // The types Add found no server stat for, with their count.
    public Dictionary<string, int> Unmapped { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Count => valueTypes.Count;

    // The table of the first reader that has it: the played client's, else the data pack's.
    public ClientSpecialAbility(params M2dReader?[] readers) {
        foreach (M2dReader? reader in readers) {
            PackFileEntry? entry = reader?.Files.FirstOrDefault(file => string.Equals(file.Name, TableName, StringComparison.OrdinalIgnoreCase));
            if (reader == null || entry == null) {
                continue;
            }
            Load(reader.GetXmlDocument(entry));
            break;
        }
    }

    public ClientSpecialAbility(XmlDocument table) {
        Load(table);
    }

    private void Load(XmlDocument table) {
        foreach (XmlNode node in table.SelectNodes("ms2/key")!) {
            if (node is XmlElement row && int.TryParse(row.GetAttribute("valueType"), out int valueType) && row.GetAttribute("option") is { Length: > 0 } option) {
                valueTypes[option] = valueType;
            }
        }
    }

    // The basic stats a client type adds to (none for a special one).
    public static IReadOnlyList<BasicAttribute> BasicAttributes(string type) => Basic.TryGetValue(type, out BasicAttribute[]? attributes) ? attributes : [];

    // Whether the client type is a rate (value type 2) or a value (1); null where the table has neither.
    public bool? Rate(string type) => valueTypes.TryGetValue(type, out int valueType) && valueType is 1 or 2 ? valueType == 2 : null;

    // Adds the client's row to the stats; false (and counted in Unmapped) where the type has no server stat or no
    // value type in the table.
    public bool Add(Stats stats, string type, int value) {
        if (value == 0) {
            return true;
        }
        if (!valueTypes.TryGetValue(type, out int valueType) || valueType is not (1 or 2)) {
            return Skip(type);
        }
        bool rate = valueType == 2;
        float scaled = rate ? value / 1000f : value;
        if (Basic.TryGetValue(type, out BasicAttribute[]? attributes)) {
            foreach (BasicAttribute attribute in attributes) {
                if (rate) {
                    stats.Rates[attribute] = stats.Rates.GetValueOrDefault(attribute) + scaled;
                } else {
                    stats.Values[attribute] = stats.Values.GetValueOrDefault(attribute) + value;
                }
            }
            return true;
        }
        if (string.Equals(type, "defensive_physicaldamage", StringComparison.OrdinalIgnoreCase) && rate) {
            stats.PhysicalDamageTakenRate += scaled;
            return true;
        }
        if (string.Equals(type, "defensive_magicaldamage", StringComparison.OrdinalIgnoreCase) && rate) {
            stats.MagicalDamageTakenRate += scaled;
            return true;
        }
        if (Conversion.TryGetValue(type, out (BasicAttribute Base, BasicAttribute Result) conversion)) {
            if (!rate || stats.Conversion != null) {
                return Skip(type);
            }
            stats.Conversion = new AdditionalEffectMetadataStatus.StatConversion(conversion.Base, conversion.Result, scaled);
            return true;
        }
        if (Special.TryGetValue(type, out SpecialAttribute special)) {
            Dictionary<SpecialAttribute, float> target = rate ? stats.SpecialRates : stats.SpecialValues;
            target[special] = target.GetValueOrDefault(special) + scaled;
            return true;
        }
        return Skip(type);
    }

    private bool Skip(string type) {
        Unmapped[type] = Unmapped.GetValueOrDefault(type) + 1;
        return false;
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model;

public static class ModelExtensions {
    public static JobCode Code(this Job job) {
        if (job == Job.GameMaster) return JobCode.GameMaster;
        return (JobCode) ((int) job / 10);
    }

    public static bool IsAwakening(this Job job) {
        return ((int) job % 10) != 0;
    }

    public static JobFilterFlag FilterFlag(this JobCode job) {
        return job switch {
            JobCode.None => JobFilterFlag.None,
            JobCode.Newbie => JobFilterFlag.Newbie,
            JobCode.Knight => JobFilterFlag.Knight,
            JobCode.Berserker => JobFilterFlag.Berserker,
            JobCode.Wizard => JobFilterFlag.Wizard,
            JobCode.Priest => JobFilterFlag.Priest,
            JobCode.Archer => JobFilterFlag.Archer,
            JobCode.HeavyGunner => JobFilterFlag.HeavyGunner,
            JobCode.Thief => JobFilterFlag.Thief,
            JobCode.Assassin => JobFilterFlag.Assassin,
            JobCode.RuneBlader => JobFilterFlag.RuneBlader,
            JobCode.Striker => JobFilterFlag.Striker,
            JobCode.SoulBinder => JobFilterFlag.SoulBinder,
            JobCode.GameMaster => JobFilterFlag.Newbie,
            _ => throw new ArgumentOutOfRangeException(nameof(job), job, "Invalid JobCode"),
        };
    }

    public static JobFilterFlag FilterFlags(this IEnumerable<JobCode> jobs) {
        return jobs.Aggregate(JobFilterFlag.None, (current, job) => current | job.FilterFlag());
    }

    public static GenderFilterFlag FilterFlag(this Gender gender) {
        return gender switch {
            Gender.Male => GenderFilterFlag.Male,
            Gender.Female => GenderFilterFlag.Female,
            Gender.All => GenderFilterFlag.All,
            _ => throw new ArgumentOutOfRangeException(nameof(gender), gender, "Invalid Gender"),
        };
    }

    // The client's tab for an item (CItemData, itemdata.cpp FUN_14053e530), by the item's <property type>;
    // every type it does not list goes to Misc. Where this server pack types items differently from the
    // client's own item data, the client's type wins: pieces (functionTags "piece") are 27 there, 10/20 are
    // 8 or 23 and 4/5 are 6 (FishingMusic), emotes 2/14 are 24 and 2/8 are 4 (Misc).
    public static InventoryType Inventory(this ItemMetadata metadata) => metadata.Property.Inventory();

    public static InventoryType Inventory(this ItemMetadataProperty property) {
        if (property.IsFragment) {
            return InventoryType.Fragment;
        }
        if (property is { Type: 10, SubType: 20 } or { Type: 4, SubType: 5 }) {
            return InventoryType.FishingMusic;
        }
        if (property is { Type: 2, SubType: 8 or 14 }) {
            return InventoryType.Misc;
        }
        return TabOfType(property.Type, property.IsSkin);
    }

    // The client's own rule: the tab by the type alone (FUN_14053e530).
    public static InventoryType TabOfType(int type, bool isSkin) {
        return type switch {
            1 => isSkin ? InventoryType.Outfit : InventoryType.Gear,
            2 => InventoryType.Consumable,
            3 => InventoryType.Quest,
            5 or 9 => InventoryType.Mount,
            6 or 8 or 12 or 23 => InventoryType.FishingMusic,
            7 => InventoryType.Badge,
            11 => InventoryType.Pets,
            13 or 14 => InventoryType.Gemstone,
            15 or 19 => InventoryType.Catalyst,
            16 => InventoryType.LifeSkill,
            20 => InventoryType.Currency,
            21 => InventoryType.Lapenshard,
            27 => InventoryType.Fragment,
            _ => InventoryType.Misc,
        };
    }

    public static EquipSlot EquipSlot(this Item item) =>
        System.Enum.IsDefined<EquipSlot>((EquipSlot) item.Slot) ? (EquipSlot) item.Slot : Enum.EquipSlot.Unknown;
    public static LapenshardSlot LapenshardSlot(this Item item) =>
        System.Enum.IsDefined<LapenshardSlot>((LapenshardSlot) item.Slot) ? (LapenshardSlot) item.Slot : default;

    public static bool IsCurrency(this Item item) => item.Id is >= 90000000 and < 100000000;
    public static bool IsMeso(this Item item) => item.Id is >= 90000001 and <= 90000003;
    public static bool IsMeret(this Item item) => item.Id is 90000004 or 90000011 or 90000015 or 90000016;
    public static bool IsExp(this Item item) => item.Id is 90000008;
    public static bool IsSpirit(this Item item) => item.Id is 90000009;
    public static bool IsStamina(this Item item) => item.Id is 90000010;
    public static bool IsEmote(this Item item) => item.Metadata.Property.Type == 2 && item.Metadata.Property.SubType == 14;
    public static bool IsExpired(this Item item) => item.ExpiryTime > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= item.ExpiryTime;

    public static ActorState State(this ActorSubState subState) {
        return subState switch {
            ActorSubState.None => ActorState.None,
            ActorSubState.Idle_Idle => ActorState.Idle,
            ActorSubState.Idle_Bore_A => ActorState.Idle,
            ActorSubState.Idle_Bore_B => ActorState.Idle,
            ActorSubState.Idle_Bore_C => ActorState.Idle,
            ActorSubState.Idle_Bore_D => ActorState.Idle,
            ActorSubState.Idle_Talk => ActorState.Idle,
            ActorSubState.Idle_Water => ActorState.Idle,
            ActorSubState.Idle_QuestCraft => ActorState.Idle,
            ActorSubState.Idle_UGCCraft => ActorState.Idle,
            ActorSubState.Idle_Fitting => ActorState.Idle,
            ActorSubState.Idle_Fitting_Basic => ActorState.Idle,
            ActorSubState.Idle_Stunt => ActorState.Idle,
            ActorSubState.Idle_CreateCharComplete => ActorState.Idle,
            ActorSubState.Idle_CharacterList => ActorState.Idle,
            ActorSubState.Idle_Happy => ActorState.Idle,
            ActorSubState.Idle_UseSkill => ActorState.Idle,
            ActorSubState.Idle_AutoRevive => ActorState.Idle,
            ActorSubState.Idle_NotUseSkill => ActorState.Idle,
            ActorSubState.Idle_Nutrient => ActorState.Idle,
            ActorSubState.Idle_CharacterSelect_Bore => ActorState.Idle,
            ActorSubState.Idle_CharacterSelect_Bore_Idle => ActorState.Idle,
            ActorSubState.Idle_ListenMusic => ActorState.Idle,
            ActorSubState.Walk_Stunt => ActorState.Walk,
            ActorSubState.Walk_Running => ActorState.Walk,
            ActorSubState.Walk_Walking => ActorState.Walk,
            ActorSubState.Walk_Booster => ActorState.Walk,
            ActorSubState.Crawl_Idle => ActorState.Crawl,
            ActorSubState.Crawl_Crawling => ActorState.Crawl,
            ActorSubState.Dash_ForwardDash => ActorState.Dash,
            ActorSubState.Dash_Falling => ActorState.Dash,
            ActorSubState.Dash_Landing => ActorState.Dash,
            ActorSubState.Dash_Hit => ActorState.Dash,
            ActorSubState.Jump_Jump => ActorState.Jump,
            ActorSubState.Jump_JumpSpecial => ActorState.Jump,
            ActorSubState.Jump_Jump2 => ActorState.Jump,
            ActorSubState.Jump_JumpSpecial2 => ActorState.Jump,
            ActorSubState.Jump_JumpRandom => ActorState.Jump,
            ActorSubState.Jump_Hit => ActorState.Jump,
            ActorSubState.Jump_Stunt => ActorState.Jump,
            ActorSubState.JumpTo_Idle => ActorState.JumpTo,
            ActorSubState.JumpTo_Done => ActorState.JumpTo,
            ActorSubState.Ladder_Idle => ActorState.Ladder,
            ActorSubState.Ladder_Up => ActorState.Ladder,
            ActorSubState.Ladder_Down => ActorState.Ladder,
            ActorSubState.Ladder_UpLand => ActorState.Ladder,
            ActorSubState.Ladder_DownLand => ActorState.Ladder,
            ActorSubState.Ladder_UpTake => ActorState.Ladder,
            ActorSubState.Ladder_DownTake => ActorState.Ladder,
            ActorSubState.Ladder_MiddleTake => ActorState.Ladder,
            ActorSubState.Ladder_Fall => ActorState.Ladder,
            ActorSubState.Rope_Idle => ActorState.Rope,
            ActorSubState.Rope_Up => ActorState.Rope,
            ActorSubState.Rope_Down => ActorState.Rope,
            ActorSubState.Rope_Middle => ActorState.Rope,
            ActorSubState.Rope_Turn => ActorState.Rope,
            ActorSubState.Rope_Take => ActorState.Rope,
            ActorSubState.Rope_Fall => ActorState.Rope,
            ActorSubState.Skill_Default => ActorState.PcSkill,
            ActorSubState.Skill_HoldAttack => ActorState.PcSkill,
            ActorSubState.Skill_MagicControl => ActorState.PcSkill,
            ActorSubState.Stun_LieStart => ActorState.Stun,
            ActorSubState.Stun_LieKeep => ActorState.Stun,
            ActorSubState.Stun_LieStop => ActorState.Stun,
            ActorSubState.Stun_Standing => ActorState.Stun,
            ActorSubState.Stun_Freezing => ActorState.Stun,
            ActorSubState.Stun_Snare => ActorState.Stun,
            ActorSubState.Stun_Vomit => ActorState.Stun,
            ActorSubState.Stun_Frozen => ActorState.Stun,
            ActorSubState.Stun_Stuck => ActorState.Stun,
            ActorSubState.Stun_Custom => ActorState.Stun,
            ActorSubState.Talk_Start => ActorState.Talk,
            ActorSubState.Talk_Loop => ActorState.Talk,
            ActorSubState.Talk_Idle => ActorState.Talk,
            ActorSubState.Talk_EnchantSuccess => ActorState.Talk,
            ActorSubState.Talk_EnchantFail => ActorState.Talk,
            ActorSubState.WaitingSelect_ChangeCap => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeIdle => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeBody => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeHead => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeHair => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeGlove => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeMantle => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeShoes => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeWeapon => ActorState.WaitingSelect,
            ActorSubState.WaitingSelect_ChangeWeaponIdle => ActorState.WaitingSelect,
            ActorSubState.Climb_Idle => ActorState.Climb,
            ActorSubState.Climb_Up => ActorState.Climb,
            ActorSubState.Climb_UpLeft => ActorState.Climb,
            ActorSubState.Climb_UpRight => ActorState.Climb,
            ActorSubState.Climb_Down => ActorState.Climb,
            ActorSubState.Climb_DownLeft => ActorState.Climb,
            ActorSubState.Climb_DownRight => ActorState.Climb,
            ActorSubState.Climb_Left => ActorState.Climb,
            ActorSubState.Climb_Right => ActorState.Climb,
            ActorSubState.Climb_UpLand => ActorState.Climb,
            ActorSubState.Climb_UpTake => ActorState.Climb,
            ActorSubState.Climb_DownTake => ActorState.Climb,
            ActorSubState.Climb_DownLand => ActorState.Climb,
            ActorSubState.Glide_Idle => ActorState.Glide,
            ActorSubState.Glide_Run => ActorState.Glide,
            ActorSubState.TaxiCall_Call => ActorState.TaxiCall,
            ActorSubState.TaxiCall_Arrive => ActorState.TaxiCall,
            ActorSubState.TaxiCall_Wait1 => ActorState.TaxiCall,
            ActorSubState.TaxiCall_Wait2 => ActorState.TaxiCall,
            ActorSubState.TaxiCall_Leave => ActorState.TaxiCall,
            ActorSubState.TaxiCall_End => ActorState.TaxiCall,
            ActorSubState.CashCall_Call => ActorState.CashCall,
            ActorSubState.CashCall_Arrive => ActorState.CashCall,
            ActorSubState.CashCall_Leave => ActorState.CashCall,
            ActorSubState.CashCall_End => ActorState.CashCall,
            ActorSubState.EmotionIdle_Idle => ActorState.EmotionIdle,
            ActorSubState.EmotionIdle_Bore_A => ActorState.EmotionIdle,
            ActorSubState.EmotionIdle_Bore_B => ActorState.EmotionIdle,
            ActorSubState.EmotionIdle_Bore_C => ActorState.EmotionIdle,
            ActorSubState.PvPWinLose_Win => ActorState.PvPWinLose,
            ActorSubState.PvPWinLose_Lose => ActorState.PvPWinLose,
            ActorSubState.Fishing_Start => ActorState.Fishing,
            ActorSubState.Fishing_Idle => ActorState.Fishing,
            ActorSubState.Fishing_Bore => ActorState.Fishing,
            ActorSubState.Fishing_FishFighting => ActorState.Fishing,
            ActorSubState.Fishing_Catch => ActorState.Fishing,
            ActorSubState.FishingFloat_Idle => ActorState.FishingFloat,
            ActorSubState.FishingFloat_Fishing => ActorState.FishingFloat,
            ActorSubState.FishingFloat_Catch => ActorState.FishingFloat,
            ActorSubState.PlayInstrument_Ready => ActorState.PlayInstrument,
            ActorSubState.PlayInstrument_Playing_Direct => ActorState.PlayInstrument,
            ActorSubState.PlayInstrument_Playing_Score_Solo => ActorState.PlayInstrument,
            ActorSubState.PlayInstrument_Ready_Score_Ensemble => ActorState.PlayInstrument,
            ActorSubState.PlayInstrument_Playing_Score_Ensemble => ActorState.PlayInstrument,
            ActorSubState.Summon_React => ActorState.Summon,
            ActorSubState.Summon_Pet => ActorState.Summon,
            ActorSubState.Swim_Swim => ActorState.Swim,
            ActorSubState.Swim_Stunt => ActorState.Swim,
            ActorSubState.Land_Land => ActorState.Land,
            ActorSubState.Land_Stunt => ActorState.Land,
            ActorSubState.HomeConvenient_Call => ActorState.HomeConvenient,
            ActorSubState.HomeConvenient_End => ActorState.HomeConvenient,
            ActorSubState.Interact_Interact => ActorState.Interaction,
            ActorSubState.Interact_Success => ActorState.Interaction,
            ActorSubState.Interact_Fail => ActorState.Interaction,
            ActorSubState.Homemade_Try => ActorState.Homemade,
            ActorSubState.Homemade_Harvest => ActorState.Homemade,
            ActorSubState.Homemade_Success => ActorState.Homemade,
            ActorSubState.Homemade_Fail => ActorState.Homemade,
            _ => ActorState.Max,
        };
    }

    // The reason an ExpUp carries. KMS2 names one on every gain; the client picks the text by it, opens the
    // reward notice for a mini game (3007) and adds the PC room suffix on a quest or a kill.
    public static ExpMessageCode Message(this ExpType type) {
        return type switch {
            ExpType.mapCommon => ExpMessageCode.s_msg_take_map_exp,
            ExpType.mapHidden => ExpMessageCode.s_msg_take_hidden_map_exp,
            ExpType.quest => ExpMessageCode.quest,
            ExpType.mission => ExpMessageCode.mission,
            ExpType.expDrop => ExpMessageCode.expDrop,
            ExpType.dungeonRelative => ExpMessageCode.dungeonRelative,
            ExpType.miniGame => ExpMessageCode.miniGame,
            ExpType.gathering => ExpMessageCode.gathering,
            ExpType.manufacturing => ExpMessageCode.manufacturing,
            ExpType.petTaming => ExpMessageCode.petTaming,
            ExpType.guildUserExp => ExpMessageCode.guildUserExp,
            ExpType.monster => ExpMessageCode.monster,
            ExpType.taxi => ExpMessageCode.s_msg_take_taxi_exp,
            ExpType.telescope => ExpMessageCode.s_msg_take_telescope_exp,
            ExpType.rareChestFirst => ExpMessageCode.s_msg_take_normal_rare_first_exp,
            ExpType.rareChest => ExpMessageCode.s_msg_take_normal_rare_exp,
            ExpType.normalChest => ExpMessageCode.s_msg_take_normal_chest_exp,
            ExpType.musicMastery1 or ExpType.musicMastery2 or ExpType.musicMastery3 or ExpType.musicMastery4 => ExpMessageCode.s_msg_take_play_instrument_exp,
            ExpType.arcade => ExpMessageCode.s_msg_take_arcade_exp,
            ExpType.fishing => ExpMessageCode.s_msg_take_fishing_exp,
            _ => ExpMessageCode.s_msg_take_exp,
        };
    }

    public static ExpType Type(this ExpMessageCode code) {
        return code switch {
            // Both map reasons count as a common map, and the reasons with the plain text as none, as before.
            ExpMessageCode.s_msg_take_map_exp or ExpMessageCode.s_msg_take_hidden_map_exp => ExpType.mapCommon,
            ExpMessageCode.s_msg_take_taxi_exp => ExpType.taxi,
            ExpMessageCode.s_msg_take_telescope_exp => ExpType.telescope,
            ExpMessageCode.s_msg_take_normal_rare_first_exp => ExpType.rareChestFirst,
            ExpMessageCode.s_msg_take_normal_rare_exp => ExpType.rareChest,
            ExpMessageCode.s_msg_take_normal_chest_exp => ExpType.normalChest,
            ExpMessageCode.s_msg_take_play_instrument_exp => ExpType.musicMastery1,
            ExpMessageCode.s_msg_take_arcade_exp => ExpType.arcade,
            ExpMessageCode.s_msg_take_fishing_exp => ExpType.fishing,
            ExpMessageCode.dungeonRelative => ExpType.dungeonClear,
            _ => ExpType.none,
        };
    }
}

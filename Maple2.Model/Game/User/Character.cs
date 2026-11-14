using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Tools;

namespace Maple2.Model.Game;

public class Character {
    #region Immutable
    public long CreationTime { get; init; }
    public DateTime LastModified { get; set; }

    public long Id { get; init; }
    public long AccountId { get; init; }
    #endregion

    public long DeleteTime;
    public long LastOnlineTime;

    public required string Name;
    public Gender Gender;
    public int MapId;
    public Job Job;

    public SkinColor SkinColor;
    public short Level = 1;
    public long Exp;
    public long RestExp;

    public int Title;
    public short Insignia;

    public int RoomId;
    // The dungeon room (DungeonRoom id) of the field the player is in, 0 outside a dungeon instance.
    public int DungeonId;
    public int InstanceMapId;
    public short Channel;
    public short ReturnChannel;

    public long StorageCooldown;
    public long DoctorCooldown;

    // The ability window: learned ability ids and the time of the last reset (seconds, 0 = never).
    public List<int> Abilities = [];
    public long AbilityResetTime;

    // Pet fusion: the pet fodders used today and when the count was last stored (seconds, 0 = never). The
    // client gets both at login (+0x11f4/+0x11f8) and the count after each fusion (RequestPet mode 12).
    public int PetFusionCount;
    public long PetFusionTime;

    // The Mini-Game M extra reward (constant userMassiveExtraRewardMax a day): the rewards taken today and when the
    // count was last stored (seconds, 0 = never).
    public int MassiveExtraRewardCount;
    public long MassiveExtraRewardTime;

    // Decorating exp (ConstructExpRules): the placements paid today, with the past-the-cap notice counted as one more,
    // and when the count was last stored (seconds, 0 = never).
    public int ConstructExpCount;
    public long ConstructExpTime;

    // The guild championship rewards taken on GuildPvpRewardDay (yyyymmdd, local), at most GuildPvp.DailyRewardCap
    // whatever the guild.
    public int GuildPvpRewardDay;
    public int GuildPvpRewardCount;

    // The sky fortress mission attacks (Quest 0x0F, constant FameContentsMissionAttackCount a day): the attacks made
    // today and when the count was last stored (seconds, 0 = never). The client keeps it as m_refreshCount.
    public int MissionAttackCount;
    public long MissionAttackTime;

    // The daily quest grants (DailyQuestGrant): the start of the last period each kind was granted for (seconds,
    // 0 = never).
    public long FieldQuestGrantTime;
    public long MissionPointGrantTime;
    public long DailyMissionGrantTime;
    public long WeddingMissionGrantTime;

    // The guild the character fights for in a guild championship season (Pvp 4, the champion guild confirm): the
    // season, the guild's id and name. ServerEnter and Pvp 4 send it while the season runs; Pvp 3 does not use it
    // (it takes the player's own guild).
    public int ChampionSeasonId;
    public long ChampionGuildId;
    public string ChampionGuildName = string.Empty;

    public LimitedStack<int> ReturnMaps = new LimitedStack<int>(3);
    public Vector3 ReturnPosition;
    public string Picture = string.Empty;
    public string Motto = string.Empty;
    public string GuildName = string.Empty;
    public long GuildId;
    public int PartyId;
    public List<long> ClubIds = [];

    // The club list goes into the user record once per club; the client stops on more entries than the club limit.
    public bool AddClubId(long clubId) {
        if (ClubIds.Contains(clubId)) {
            return false;
        }
        ClubIds.Add(clubId);
        return true;
    }
    public required Mastery Mastery;
    public AchievementInfo AchievementInfo;
    public MarriageInfo MarriageInfo = new();
    public readonly Dictionary<int, DungeonEnterLimit> DungeonEnterLimits = [];
    public short DeathCount;
    public long DeathTick;
    public DeathState DeathState;
    public long PremiumTime;
    // The second outfit tab (the client's codi tab, feature Skin2tab): unix seconds.
    // Start 0 = never activated; End 0 with a Start = no end.
    public long SkinTabStart;
    public long SkinTabEnd;
    public MentorRole MentorRole;
}

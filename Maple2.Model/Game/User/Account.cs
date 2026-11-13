using Maple2.Model.Enum;

namespace Maple2.Model.Game;

public class Account {
    #region Immutable
    public DateTime LastModified { get; set; }
    public long Id { get; init; }

    public required string Username { get; init; }
    public Guid MachineId { get; set; }
    #endregion

    public int MaxCharacters { get; set; }
    public int PrestigeLevel { get; set; }
    public int PrestigeLevelsGained { get; set; }
    public long PrestigeExp { get; set; }
    public long PrestigeCurrentExp { get; set; }
    public IList<PrestigeMission> PrestigeMissions { get; set; }
    public IList<int> PrestigeRewardsClaimed { get; set; }
    public long PremiumTime { get; set; }
    // The birthday as unix seconds of 12:00 UTC on its month and day in 2000 (the year is not kept); 0 = none.
    // Set only by the GM command; the client reads it as the character's birthday (BirthdayRules).
    public long Birthday { get; set; }
    public IList<int> PremiumRewardsClaimed { get; set; }
    // The VIP goods shop's purchase counts (VipGoodsRules).
    public IList<VipGoodsPurchase> VipGoodsPurchases { get; set; }
    public int MesoMarketListed { get; set; }
    public int MesoMarketPurchased { get; set; }

    public int SurvivalLevel { get; set; }
    public long SurvivalExp { get; set; }
    public int SurvivalSilverLevelRewardClaimed { get; set; }
    public int SurvivalGoldLevelRewardClaimed { get; set; }
    public bool ActiveGoldPass { get; set; }
    public bool Online { get; set; }
    public AdminPermissions AdminPermissions { get; set; }

    public Account() {
        PremiumRewardsClaimed = new List<int>();
        VipGoodsPurchases = new List<VipGoodsPurchase>();
        PrestigeMissions = new List<PrestigeMission>();
        PrestigeRewardsClaimed = new List<int>();
        AdminPermissions = AdminPermissions.None;
    }
}

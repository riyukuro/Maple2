using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;

namespace Maple2.Model.Validators;

// The build budget and the build helpers of a home: RequestCube 56 (budget), 57 (add a helper) and 58 (remove
// a helper). The client shows the answers' error byte through the build notices (CCubeMapInformationService
// ShowBuildNotice, cubemapinformationservice_head.cpp:306), which have a text for a short budget (96, 97)
// and none for a refused helper, so those refusals use the code the client shows nothing for.
public static class HomeBuildRules {
    // Server limit: the client has no limit of its own on the helper list.
    public const int MaxBuilders = 20;

    public const UgcMapError Refused = UgcMapError.s_empty_string;

    // RequestCube 56 sets the budget to the two values. The difference to the stored budget comes out of the
    // owner's wallet, or goes back to it when the budget is lowered.
    // A lowered budget is refused when the wallet cannot hold what comes back (its cap): the wallet's setters
    // cut at the cap and the difference would be lost.
    public static UgcMapError CheckBudget(long meso, long merat, long budgetMeso, long budgetMerat, long walletMeso, long walletMerat,
                                          long maxMeso = long.MaxValue, long maxMerat = long.MaxValue) {
        if (meso < 0 || merat < 0) {
            return Refused;
        }
        if (budgetMeso - meso > maxMeso - walletMeso || budgetMerat - merat > maxMerat - walletMerat) {
            return Refused;
        }
        if (meso - budgetMeso > walletMeso) {
            return UgcMapError.s_err_ugcmap_not_enough_meso_balance;
        }
        if (merat - budgetMerat > walletMerat) {
            return UgcMapError.s_err_ugcmap_not_enough_merat_balance;
        }
        return UgcMapError.s_ugcmap_ok;
    }

    public static UgcMapError CheckAdd(long ownerId, long targetId, ICollection<long> builders) {
        if (targetId == 0 || targetId == ownerId || builders.Contains(targetId) || builders.Count >= MaxBuilders) {
            return Refused;
        }
        return UgcMapError.s_ugcmap_ok;
    }

    public static UgcMapError CheckRemove(long targetId, ICollection<long> builders) {
        return targetId != 0 && builders.Contains(targetId) ? UgcMapError.s_ugcmap_ok : Refused;
    }

    // Whether an account in the owner's home is told it may build there (ResponseCube 0x35): a listed helper,
    // and not while the home is in a planner mode. The same answer on every path that tells a helper.
    public static bool MayGrant(bool planner, bool isBuilder) {
        return isBuilder && !planner;
    }

    // The trophy grades cube items ask for that the home owner has not cleared, in key order: the set a build
    // helper's client locks cubes by (ResponseCube 0x37).
    public static List<(int TrophyId, int Grade)> MissingTrophies(IEnumerable<(int TrophyId, int Grade)> required, Func<int, int, bool> cleared) {
        return required.Where(key => key.TrophyId > 0 && !cleared(key.TrophyId, key.Grade)).Distinct().Order().ToList();
    }

    // A cube a helper places is bought from the budget, in the currency of its shop entry.
    public static UgcMapError Charge(Home home, bool merat, long price) {
        UgcMapError error = CanCharge(home, merat, price);
        if (error != UgcMapError.s_ugcmap_ok || price <= 0) {
            return error;
        }
        if (merat) {
            home.BuildMerat -= price;
        } else {
            home.BuildMeso -= price;
        }
        return UgcMapError.s_ugcmap_ok;
    }

    // Whether Charge would take the price; changes nothing. Asked before anything on the plot is touched.
    public static UgcMapError CanCharge(Home home, bool merat, long price) {
        if (price <= 0) {
            return UgcMapError.s_ugcmap_ok;
        }
        if (merat) {
            return home.BuildMerat < price ? UgcMapError.s_err_ugcmap_not_enough_merat_balance : UgcMapError.s_ugcmap_ok;
        }
        return home.BuildMeso < price ? UgcMapError.s_err_ugcmap_not_enough_meso_balance : UgcMapError.s_ugcmap_ok;
    }

    // Gives a charge back when the cube could not be placed after it.
    public static void Refund(Home home, bool merat, long price) {
        if (price <= 0) {
            return;
        }
        if (merat) {
            home.BuildMerat += price;
        } else {
            home.BuildMeso += price;
        }
    }

    // buy_cube (74000028 "Spend 5,000 mesos placing furnishings in your house", value 5000): the mesos a player
    // pays for a furnishing bought as it is placed. A meret price spends no mesos.
    public static long BuyCubeMesos(FurnishingCurrencyType currency, long price) {
        return currency == FurnishingCurrencyType.Meso && price > 0 ? price : 0;
    }
}

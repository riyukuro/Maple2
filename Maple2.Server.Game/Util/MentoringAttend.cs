using Maple2.Database.Extensions;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The mentee's attend reward (UIMentoringRewardPage, uimentoringrewardpage.cpp). The client holds the streak
// level and the time of the next claim (Mentor 10, CMentoringManager::OnSetDate):
// - the button is enabled from that time on (FUN_140b71610: next <= now);
// - the coin shown is the table's coin of the level (FUN_140b715c0), and level 0 once a whole day has passed
//   since that time (GetDaysBetween, timeutil.cpp:489), so a missed day starts the streak again;
// - the click sends Mentor 0.
public static class MentoringAttend {
    // The coins and caps are table/mentoringconfig.xml's (MentoringConfigTable).

    // The mentor point item (InventoryManager.AddCurrency: MentorToken).
    public const int MentorPointItemId = 90000023;

    private const long DaySeconds = 24 * 60 * 60;

    public static bool Available(long next, long now) => next <= now;

    // The level the client shows at now, as FUN_140b715c0 computes it, for a table of `coins` attend coins.
    public static int Level(int level, long next, long now, int coins) {
        if (Math.Abs(now - next) / DaySeconds > 0) {
            return 0;
        }
        return Math.Clamp(level, 0, Math.Max(coins - 1, 0));
    }

    // A claim at now: the coin given, the level stored for the next claim (the third coin goes on until a day is
    // missed) and the next claim time, the next midnight.
    public static (int Coin, int Level, long Next) Claim(MentoringConfigTable table, int level, long next, DateTime now) {
        int coins = table.AttendRewardCoins.Count;
        int current = Level(level, next, now.ToEpochSeconds(), coins);
        long nextClaim = now.Date.AddDays(1).ToEpochSeconds();
        return (table.AttendRewardCoin(current), Math.Min(current + 1, Math.Max(coins - 1, 0)), nextClaim);
    }
}

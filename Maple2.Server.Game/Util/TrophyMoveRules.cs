namespace Maple2.Server.Game.Util;

/// <summary>
/// Distance counted for movement trophies and quests (run, swim, climb, glide, riding, crawl, fall). KMS2 counts one
/// for each 100 game units moved in the state and sends the counts once five or more are due (steps of 5, at times
/// 6 to 9, about 500 units each for glide, run and riding in the KMS2 captures).
/// </summary>
public static class TrophyMoveRules {
    public const float UnitDistance = 100f;
    public const int MinStep = 5;

    /// <summary>
    /// Adds a move to a state's distance and returns the counts to report: 0 until five are due, then every whole
    /// count; the rest of a count is kept.
    /// </summary>
    public static int Counts(ref float accumulated, float distance) {
        if (distance > 0 && float.IsFinite(distance)) {
            accumulated += distance;
        }
        int counts = (int) (accumulated / UnitDistance);
        if (counts < MinStep) {
            return 0;
        }
        accumulated -= counts * UnitDistance;
        return counts;
    }
}

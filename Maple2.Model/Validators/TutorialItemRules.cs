namespace Maple2.Model.Validators;

// RequestTutorialItem: no body. The job's start items are given up to their count.
public static class TutorialItemRules {
    // How many pieces of one start item are still to give: its rows' counts together, less what the player has.
    public static int Missing(int owned, IEnumerable<int> counts) {
        long wanted = 0;
        foreach (int count in counts) {
            if (count > 0) {
                wanted += count;
            }
        }
        long missing = wanted - Math.Max(0, owned);
        return (int) Math.Clamp(missing, 0, int.MaxValue);
    }
}

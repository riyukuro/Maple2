namespace Maple2.Server.Game.Util;

public static class AiSelect {
    /// The weight of each child of an AI select node.
    /// With useNpcProb="1" child i is weighted by the npc's own prob for its skill i + 1 (npc xml skill probs); the
    /// default AI trees put one branch per skill index under such a select, and a child past the npc's skills gets 0.
    /// Otherwise (or when the npc carries no probs, as in data ingested before the probs were kept) the node's own prob
    /// list is used, one value per child.
    public static int[] Weights(int[] prob, bool useNpcProb, IReadOnlyList<int> npcProbs, int childCount) {
        var weights = new int[childCount];
        if (useNpcProb && npcProbs.Any(value => value > 0)) {
            for (int i = 0; i < childCount && i < npcProbs.Count; i++) {
                weights[i] = Math.Max(0, npcProbs[i]);
            }
            return weights;
        }

        for (int i = 0; i < childCount && i < prob.Length; i++) {
            weights[i] = Math.Max(0, prob[i]);
        }
        return weights;
    }
}

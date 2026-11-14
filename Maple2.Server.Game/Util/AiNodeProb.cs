using static Maple2.Model.Metadata.AiMetadata;

namespace Maple2.Server.Game.Util;

public static class AiNodeProb {
    /// <summary>
    /// prob on a node other than select is the chance in 100 that the node runs; one that does not run skips its
    /// children too. KR AI_TurkaReaperCostume_SummonTypeA.xml comments its buff prob="60" and its four standby
    /// prob="10" nodes as random: a reflect buff most summons get and a random wait, so the summons do not all act
    /// alike. The ingest gives 100 to a node without prob (75644 of the 75721 KR
    /// values); the rest are 25, 40 (AI_Mason_FR_Lv1 teleport and skill), 33, 10, 34, 60 and 20.
    /// </summary>
    public static int Of(Entry entry) => entry switch {
        SkillNode node => node.Prob,
        TeleportNode node => node.Prob,
        StandbyNode node => node.Prob,
        TargetNode node => node.Prob,
        SayNode node => node.Prob,
        MoveNode node => node.Prob,
        BuffNode node => node.Prob,
        _ => 100,
    };

    // No KR file gives 0 (the lowest value is 10); a node record built without a prob (0) runs, as one without the
    // attribute does.
    public static bool Runs(int prob, Random random) => prob is >= 100 or <= 0 || random.Next(100) < prob;
}

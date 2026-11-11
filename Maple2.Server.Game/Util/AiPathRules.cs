using System.Text.RegularExpressions;

namespace Maple2.Server.Game.Util;

public static partial class AiPathRules {
    // Some npc rows name their AI after the npc they summon: npc 21420503 has aiInfo path
    // "AI_Default_Summon_24003227.xml" with summonNpcIDs="24003227". No file of that name ships; the server
    // data has the one template AI_Default_Summon.xml, whose battleEnd summon node takes npcId="1", the first
    // of the row's summonNpcIDs. So a missing name ending in _<digits> falls back to the name without that
    // suffix when that file exists.
    public static string? Resolve(string aiPath, Func<string, bool> exists) {
        if (exists(aiPath)) {
            return aiPath;
        }
        Match match = SummonSuffix().Match(aiPath);
        if (match.Success) {
            string template = match.Groups[1].Value + ".xml";
            if (exists(template)) {
                return template;
            }
        }
        return null;
    }

    [GeneratedRegex(@"^(.+)_\d+\.xml$")]
    private static partial Regex SummonSuffix();
}

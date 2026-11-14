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
        string? normal = NormalFolder(aiPath);
        if (normal != null && exists(normal)) {
            return normal;
        }
        string? plain = WithoutLocale(aiPath);
        if (plain != null && exists(plain)) {
            return plain;
        }
        string? source = QuestSource(aiPath);
        if (source != null && exists(source)) {
            return source;
        }
        return null;
    }

    // Npc 23200077 names "BossDungeon/Papulatus_Chaos/AI_Papulatus_ChaosNA.xml", which does not ship; the folder
    // has AI_Papulatus_Chaos.xml (and AI_Papulatus_ChaosNormalJP.xml, which uses a skill index the npc's skills
    // lack the skill for). With the npc's own skill list AI_Papulatus_Chaos.xml holds every skill and level KMS2
    // sends for this npc, each inside the file's health bounds. So a missing name ending in NA.xml falls back to
    // the name without NA.
    public static string? WithoutLocale(string aiPath) {
        const string suffix = "NA.xml";
        return aiPath.EndsWith(suffix) ? aiPath[..^suffix.Length] + ".xml" : null;
    }

    // Some boss npc rows name a file in BossDungeon/<boss>/ that ships only in the copy folder
    // BossDungeon/<boss>_Normal/: npc 23000114 (room 23021003) has "BossDungeon/SnowKing/AI_SnowKing_Chaos.xml",
    // npc 23200067 "BossDungeon/Madria/AI_BigWingChimera_Chaos.xml", npcs 21500830-21500839
    // "BossDungeon/Pinkbean/Chaos/AI_StatueHuman0*.xml". Each file both folders ship is the same in both, so the
    // missing name falls back to the same path in the _Normal folder.
    public static string? NormalFolder(string aiPath) {
        string[] parts = aiPath.Split('/');
        if (parts.Length < 3 || parts[0] != "BossDungeon" || parts[1].EndsWith("_Normal")) {
            return null;
        }
        parts[1] += "_Normal";
        return string.Join('/', parts);
    }

    // A <name>_Quest folder is the quest copy of the folder <name>: BossDungeon/Archeon/Boss_Quest/ ships 10 files, 9 of
    // them the same bytes as the file of that name in BossDungeon/Archeon/Boss/ (AI_Archeon.xml is the quest's own boss).
    // Npc 25030011 (the quest map 52100301) names "BossDungeon/Archeon/Boss_Quest/AI_DarkEgoWeapon_Bow.xml", which was
    // never copied; its boss twin 23503011, with the same skill list and feature, names the file in Boss/. So a missing
    // name in a _Quest folder falls back to the same name in the folder it copies.
    public static string? QuestSource(string aiPath) {
        const string suffix = "_Quest";
        string[] parts = aiPath.Split('/');
        bool changed = false;
        for (int i = 0; i < parts.Length - 1; i++) {
            if (parts[i].Length > suffix.Length && parts[i].EndsWith(suffix)) {
                parts[i] = parts[i][..^suffix.Length];
                changed = true;
            }
        }
        return changed ? string.Join('/', parts) : null;
    }

    [GeneratedRegex(@"^(.+)_\d+\.xml$")]
    private static partial Regex SummonSuffix();
}

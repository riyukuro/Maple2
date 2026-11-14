using Maple2.Model.Metadata;
using Maple2.Server.Game.LuaFunctions;

namespace Maple2.Server.Game.Util;

// The npcs a region spawn's tags pick and their weights, as the client works them out (CNpcDataTable::FindSpawnResult,
// commonfiles/npcdata.cpp). Each tag of the row is looked up among the npcs' main tags and their sub tags; an npc
// whose difficulty is outside the row's range is not counted. An npc is kept only when the row names every one of its
// main tags (an npc without main tags comes in on a sub tag alone), and its weight is CalcNpcSpawnWeight of the main
// and sub tags the row names, its rare degree and the row's max difficulty less its own.
public static class RegionSpawnRules {
    // The row's tags the npc has among its main tags and among its sub tags, each counted per tag of the row.
    public static (int Main, int Sub) Hits(NpcMetadataBasic basic, IEnumerable<string> tags) {
        int main = 0;
        int sub = 0;
        foreach (string tag in tags) {
            main += basic.MainTags.Count(npcTag => npcTag == tag);
            sub += basic.SubTags.Count(npcTag => npcTag == tag);
        }
        return (main, sub);
    }

    // The npc's weight, or null when the row does not spawn it. The client's CLua::CalcNpcSpawnWeight returns the
    // script's number cut to an int (commonfiles/luautil.cpp), so a rare degree's share below 1 is dropped.
    public static int? Weight(NpcMetadataBasic basic, IEnumerable<string> tags, int minDifficulty, int maxDifficulty) {
        if (basic.Difficulty < minDifficulty || basic.Difficulty > maxDifficulty) {
            return null;
        }
        (int main, int sub) = Hits(basic, tags);
        if (main == 0 && sub == 0) {
            return null;
        }
        if (main != basic.MainTags.Length) {
            return null;
        }
        return (int) Lua.CalcNpcSpawnWeight(main, sub, basic.RareDegree, maxDifficulty - basic.Difficulty);
    }
}

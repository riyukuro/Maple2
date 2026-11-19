namespace Maple2.Model.Metadata;

// table/Server/{locale}/expcontrol.xml and mesocontrol.xml (<exp lv .../>, <meso lv .../>): a rate per reward kind at
// each level 1 to 99. The client keeps 11 exp kinds and 3 meso kinds (CGloabalFactor m_fExpRate[11][100] and
// m_fMesoRate[3][100], cgloabalfactor.h:57), and its quest kinds are the files' quest columns in column order: an epic
// quest reads exp kind 5 and meso kind 0, a world quest 6 and 1, a navigator mission 7 and 2 (QuestDataTable::
// FUN_14074ee30, questdata.cpp:2361-2373), the questReward_epic, _world and _guide columns of each file. So a kind is
// its column's index. A rate the file lacks is 1.0, as the client starts every rate (ResetExpRates and ResetMesoRates,
// gloabalfactor.cpp:72 and 84). KR and NA give 1.0 everywhere; CN 0.8 for the common and elite npc kills.
// ExpRates[kind][level] and MesoRates[kind][level]; level 0 is unused, as in the client.
public record GlobalFactorTable(float[][] ExpRates, float[][] MesoRates) : ServerTable {
    public const int MaxLevel = 99;

    public static readonly string[] ExpKinds = [
        "npcReward_common", "npcReward_dungeonElite", "npcReward_elite", "npcReward_boss", "npcReward_dungeonBoss",
        "questReward_epic", "questReward_world", "questReward_guide", "miniGame", "fishing", "instrument",
    ];
    public static readonly string[] MesoKinds = ["questReward_epic", "questReward_world", "questReward_guide"];

    // Every rate 1.0.
    public static GlobalFactorTable Default => new(Ones(ExpKinds.Length), Ones(MesoKinds.Length));

    public static float[][] Ones(int kinds) {
        return Enumerable.Range(0, kinds).Select(_ => Enumerable.Repeat(1f, MaxLevel + 1).ToArray()).ToArray();
    }

    // 1.0 outside the table, as the client's GetExpRate and GetMesoRate outside levels 1 to the cap.
    public float ExpRate(int kind, int level) => Rate(ExpRates, kind, level);
    public float MesoRate(int kind, int level) => Rate(MesoRates, kind, level);

    private static float Rate(float[][] rates, int kind, int level) {
        if (kind < 0 || kind >= rates.Length || level < 1 || level >= rates[kind].Length) {
            return 1f;
        }
        return rates[kind][level];
    }
}

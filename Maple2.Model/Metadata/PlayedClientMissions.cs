using System.Xml;

namespace Maple2.Model.Metadata;

// The dungeon missions the played client can build, read from that client's own mission table
// (table/dungeonmission.xml). The client builds each mission id of RoomStageDungeon 0 with
// DungeonMissionCli::Create (client/ms2/dungeonmissioncli.cpp:84) and asserts when it gets null
// (RoomStageDungeonCli::OnInitMissions, roomstagedungeoncli.cpp:104, MS2_ASSERT_AT 0x68). It gets null when its table has no
// mission for the id, or when no mission class is registered under "DungeonMission" + the row's type.
// The table (DungeonMissionDataTable::ReadFromXml, commonfiles/dungeonmissiondatatable.cpp:73) keeps a
// <DungeonMission> row only when it passes IsElementAvailable (commonfiles/commonutil.cpp:1047: a set
// locale must be the client's, a set feature must be on), has a type (DungeonMissionData::Create) and an
// id other than 0; the first kept row of an id stays (std::map insert).
public static class PlayedClientMissions {
    public const string MissionTable = "table/dungeonmission.xml";

    // The mission classes the client registers (dungeonmissioncli.cpp:59-74), without the prefix.
    public static readonly IReadOnlySet<string> ClientClasses = new HashSet<string>(StringComparer.Ordinal) {
        "DamageBySkill", "DamageCount", "DamageToNpc", "DeathCount", "Event", "GainBuff", "HealingPotion", "KillNpc",
        "LastHitNpc", "LimitUserCount", "PlayTime", "Trigger", "UseEp", "UseInteractObject", "UseSkill", "UseSp",
    };

    // The ids the client builds. available is the row filter (feature and locale).
    public static HashSet<int> Buildable(XmlDocument missionTable, Func<XmlElement, bool> available) {
        var types = new Dictionary<uint, string>();
        XmlNodeList? rows = missionTable.SelectNodes("ms2/DungeonMission");
        if (rows != null) {
            foreach (XmlNode node in rows) {
                if (node is not XmlElement row || !available(row)) {
                    continue;
                }
                string type = row.GetAttribute("type");
                if (type.Length == 0 || !uint.TryParse(row.GetAttribute("id"), out uint id) || id == 0) {
                    continue;
                }
                types.TryAdd(id, type);
            }
        }
        var ids = new HashSet<int>();
        foreach ((uint id, string type) in types) {
            if (ClientClasses.Contains(type)) {
                ids.Add((int) id);
            }
        }
        return ids;
    }

    // The server's missions without those the played client cannot build.
    public static Dictionary<int, T> Keep<T>(IReadOnlyDictionary<int, T> missions, IReadOnlySet<int> buildable) {
        return missions.Where(entry => buildable.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value);
    }
}

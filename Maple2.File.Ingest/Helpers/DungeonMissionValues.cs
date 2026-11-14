using System.Globalization;
using System.Xml;
using M2dXmlGenerator;

namespace Maple2.File.Ingest.Helpers;

// The value1 of each dungeon mission (table/dungeonmission.xml) as whole numbers. The client reads it as a comma list
// of unsigned ids (LastHitNpc, KillNpc, GainBuff, DamageBySkill: XmlTokenList into std::set<unsigned>) or as one
// __int64 total (DamageToNpc), commonfiles/dungeonmissiondata.cpp. The parser's row has value1 as floats, which move
// ids above 2^24 (23200083 reads as 23200084), so the npc a LastHitNpc mission names never dies. A row is read when it
// is available for the locale and features (DungeonMissionDataTable::ReadFromXml keeps the first available row of an
// id); a fraction is cut, as the integer read stops at the point.
public static class DungeonMissionValues {
    public static Dictionary<int, long[]> Value1(XmlDocument table) {
        var results = new Dictionary<int, long[]>();
        XmlNodeList? rows = table.SelectNodes("ms2/DungeonMission");
        if (rows == null) {
            return results;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature"))
                || !FeatureLocaleFilter.HasLocale(row.GetAttribute("locale"))) {
                continue;
            }
            if (!int.TryParse(row.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id == 0) {
                continue;
            }
            var values = new List<long>();
            foreach (string token in row.GetAttribute("value1").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
                if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)) {
                    values.Add((long) decimal.Truncate(value));
                }
            }
            results.TryAdd(id, values.ToArray());
        }
        return results;
    }
}

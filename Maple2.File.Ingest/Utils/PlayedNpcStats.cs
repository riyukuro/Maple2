using System.Xml;
using System.Xml.Serialization;
using Maple2.File.Parser.Xml.Npc;

namespace Maple2.File.Ingest.Utils;

/// <summary>
/// The stat block the played client holds for each NPC, read from that client's npcdata/&lt;nnn&gt;.xml the way the
/// client reads it. CNpcData::Load (commonfiles/npcdata.cpp:771) keeps the &lt;environment&gt; that FUN_14028f8d0
/// (commonfiles/commonutil.cpp:1064) selects among the ones that pass the locale and feature test:
/// CFeatureManager::SelectFeatureElement takes the one whose feature has the higher level, then the one whose locale
/// is the client's, then one without a locale, else the first. CNpcData::LoadXml (npcdata.cpp:1529) reads each stat
/// of statStringTable.xml from its &lt;stat&gt; by name and adds nothing to it: no hidden add of any feature. When a
/// file holds an id twice, NpcDataTableCli::Load keeps the later one.
/// </summary>
public static class PlayedNpcStats {
    private static readonly XmlSerializer Serializer = new(typeof(Stat), new XmlRootAttribute("stat") { Namespace = "" });

    /// <param name="npcFiles">The played client's npcdata files.</param>
    /// <param name="available">The environment filter (feature and locale).</param>
    /// <param name="featureLevel">The level of an enabled feature (0 when unknown or empty).</param>
    /// <param name="locale">The locale the data is ingested for.</param>
    public static Dictionary<int, Stat> Read(IEnumerable<XmlDocument> npcFiles, Func<XmlElement, bool> available,
            Func<string, int> featureLevel, string locale) {
        var result = new Dictionary<int, Stat>();
        foreach (XmlDocument document in npcFiles) {
            XmlNodeList? npcs = document.DocumentElement?.SelectNodes("npc");
            if (npcs == null) {
                continue;
            }
            foreach (XmlElement npc in npcs.OfType<XmlElement>()) {
                if (!int.TryParse(npc.GetAttribute("id"), out int id) || id == 0) {
                    continue;
                }
                XmlElement? environment = SelectEnvironment(npc, available, featureLevel, locale);
                if (environment == null) {
                    continue;
                }
                result[id] = ParseStat(environment["stat"]);
            }
        }
        return result;
    }

    public static XmlElement? SelectEnvironment(XmlElement npc, Func<XmlElement, bool> available, Func<string, int> featureLevel,
            string locale) {
        XmlElement? selected = null;
        foreach (XmlElement environment in npc.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "environment")) {
            if (!available(environment)) {
                continue;
            }
            selected = selected == null ? environment : SelectFeatureElement(selected, environment, featureLevel, locale);
        }
        return selected;
    }

    // CFeatureManager::SelectFeatureElement (featuremanager.cpp:1049) with SelectHigherLevelFeatureName (:1012) and
    // FUN_14036dfa0 (:1032): of two available rows with one key, the one the client keeps.
    public static XmlElement SelectFeatureElement(XmlElement a, XmlElement b, Func<string, int> featureLevel, string locale) {
        string featureA = a.GetAttribute("feature");
        string featureB = b.GetAttribute("feature");
        if (featureA != featureB) {
            int levelA = featureLevel(featureA);
            int levelB = featureLevel(featureB);
            if (levelA != levelB) {
                return levelA > levelB ? a : b;
            }
            return string.CompareOrdinal(featureA, featureB) > 0 ? a : b;
        }
        string localeA = a.GetAttribute("locale");
        string localeB = b.GetAttribute("locale");
        if (localeA == localeB || localeA == locale) {
            return a;
        }
        if (localeB == locale) {
            return b;
        }
        if (localeA.Length == 0) {
            return a;
        }
        return localeB.Length == 0 ? b : a;
    }

    // An NPC without a <stat> keeps every stat at 0, as the client's does.
    public static Stat ParseStat(XmlElement? stat) {
        if (stat == null) {
            return new Stat();
        }
        using var reader = new XmlNodeReader(stat);
        return (Stat) Serializer.Deserialize(reader)!;
    }
}

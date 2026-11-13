using System.Xml;

namespace Maple2.Model.Metadata;

// The quests the played client knows, read from that client's own quest table (questdata/questdata.xml). The client
// keeps a <quest> only when its feature is enabled (QuestDataTable::LoadQuestData, commonfiles/questdata.cpp:1797,
// with IsElementFeatureEnabled, commonfiles/commonutil.cpp:1034; no locale test); CQuest::Init
// (client/ms2/questmanager_1.cpp:127) fails for any other id. The quest list packet (Quest 0x16,
// CQuestManager::OnQuest @0x1416cbd90, client/ms2/questmanager_2.cpp) reads a quest's progress only after Init
// succeeded, so one unknown quest in the list shifts the read of every quest after it.
public static class PlayedClientQuests {
    public const string QuestTable = "questdata/questdata.xml";

    // The quest ids of the table. available is the feature filter.
    public static HashSet<int> Ids(XmlDocument questTable, Func<XmlElement, bool> available) {
        var ids = new HashSet<int>();
        XmlNodeList? quests = questTable.SelectNodes("ms2/quest");
        if (quests == null) {
            return ids;
        }
        foreach (XmlNode node in quests) {
            if (node is XmlElement quest && int.TryParse(quest.GetAttribute("id"), out int id) && id != 0 && available(quest)) {
                ids.Add(id);
            }
        }
        return ids;
    }

    // The start and complete npc of each quest of the table (<start npc>, <complete npc>; 0 when missing). The client
    // marks an npc with a quest's "!" or "?" and offers the quest in its talk only at these npcs
    // (CQuestManager::GetNpcQuestState, client/ms2/questmanager_2.cpp:1884, keyed by QuestData start and complete
    // npc), so the server takes them from the played client's table and not from its own quest files.
    public static Dictionary<int, (int Start, int Complete)> Npcs(XmlDocument questTable, Func<XmlElement, bool> available) {
        var npcs = new Dictionary<int, (int Start, int Complete)>();
        XmlNodeList? quests = questTable.SelectNodes("ms2/quest");
        if (quests == null) {
            return npcs;
        }
        foreach (XmlNode node in quests) {
            if (node is XmlElement quest && int.TryParse(quest.GetAttribute("id"), out int id) && id != 0 && available(quest)) {
                npcs[id] = (Npc(quest, "start"), Npc(quest, "complete"));
            }
        }
        return npcs;
    }

    // The requires the server's quest parser does not read, from the table's <require> of each quest: guildLevel (-1
    // without the attribute, as QuestData keeps it; 0 is a require, no guild), vip and equipment.
    public static Dictionary<int, PlayedRequire> Requires(XmlDocument questTable, Func<XmlElement, bool> available) {
        var requires = new Dictionary<int, PlayedRequire>();
        XmlNodeList? quests = questTable.SelectNodes("ms2/quest");
        if (quests == null) {
            return requires;
        }
        foreach (XmlNode node in quests) {
            if (node is not XmlElement quest || !int.TryParse(quest.GetAttribute("id"), out int id) || id == 0 || !available(quest)
                || quest.SelectSingleNode("require") is not XmlElement require) {
                continue;
            }
            int guildLevel = require.HasAttribute("guildLevel") && int.TryParse(require.GetAttribute("guildLevel"), out int level) ? level : -1;
            bool vip = int.TryParse(require.GetAttribute("vip"), out int flag) && flag != 0;
            int[] equipment = require.GetAttribute("equipment").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => int.TryParse(item, out int itemId) ? itemId : 0).Where(itemId => itemId != 0).ToArray();
            if (guildLevel != -1 || vip || equipment.Length > 0) {
                requires[id] = new PlayedRequire(guildLevel, vip, equipment);
            }
        }
        return requires;
    }

    public record PlayedRequire(int GuildLevel, bool Vip, int[] Equipment);

    private static int Npc(XmlElement quest, string element) =>
        quest.SelectSingleNode(element) is XmlElement node && int.TryParse(node.GetAttribute("npc"), out int npc) ? npc : 0;

    // Whether the server may start, list or send this quest: not when the played client lacks it
    // (QuestMetadataBasic.NotInPlayedClient). Without the mark every quest can be.
    public static bool Known(QuestMetadataBasic basic) => !basic.NotInPlayedClient;
}

using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The server pack's npc group table (table/Server/groupSpawn.xml): <group groupId> rows of <npc id tag score>, tag a
// comma list (sf, dungeon, steampunk, temple, toy, skyfotress...). An EventSpawnGroupNPC map point names group ids with
// a count and a weight and one tag; each of its groups holds the npcs of that group id whose tags hold the point's tag.
// The parser has no type for this file. The score (Dark Stream points per npc) is not read: the trigger that spawns a
// Dark Stream point gives the point's score itself (dark_stream_spawn_monster).
public static class ServerGroupSpawn {
    public const string File = "table/Server/groupSpawn.xml";

    public readonly record struct Npc(int Id, string[] Tags);

    public static Dictionary<int, List<Npc>> Read(M2dReader? reader) {
        PackFileEntry? entry = reader?.Files.FirstOrDefault(e => e.Name.EndsWith(File, StringComparison.OrdinalIgnoreCase));
        if (reader == null || entry == null) {
            // Without the table no EventSpawnGroupNPC point has a group, and the mapper drops every such point.
            Console.WriteLine($"Warning: {File} not found; EventSpawnGroupNPC points (Dark Stream) are not ingested.");
            return new Dictionary<int, List<Npc>>();
        }
        return Read(reader.GetXmlReader(entry));
    }

    public static Dictionary<int, List<Npc>> Read(XmlReader xml) {
        var groups = new Dictionary<int, List<Npc>>();
        List<Npc>? group = null;
        while (xml.Read()) {
            if (xml.NodeType != XmlNodeType.Element) {
                continue;
            }
            if (xml.Name == "group") {
                group = int.TryParse(xml.GetAttribute("groupId"), out int groupId) ? groups.GetValueOrDefault(groupId) ?? (groups[groupId] = []) : null;
            } else if (xml.Name == "npc" && group != null && int.TryParse(xml.GetAttribute("id"), out int npcId)) {
                string[] tags = (xml.GetAttribute("tag") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                group.Add(new Npc(npcId, tags));
            }
        }
        return groups;
    }

    // The point's groups in group id order: each with its npcs of the tag that the npc table holds. A group with no
    // such npc is left out; a weight the point does not list is 0 (52010038 has one point whose only weight is under
    // the index 104111 instead of 111: GroupSpawnRules then picks among equal weights).
    public static List<SpawnGroupEntry> Groups(IReadOnlyDictionary<int, List<Npc>> table, string tag, IEnumerable<(int GroupId, int Count)> counts,
                                               IReadOnlyDictionary<int, int> weights, Func<int, bool> npcExists) {
        var result = new List<SpawnGroupEntry>();
        foreach ((int groupId, int count) in counts.OrderBy(entry => entry.GroupId)) {
            if (count <= 0 || !table.TryGetValue(groupId, out List<Npc>? npcs)) {
                continue;
            }
            int[] npcIds = npcs.Where(npc => npc.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) && npcExists(npc.Id))
                .Select(npc => npc.Id).ToArray();
            if (npcIds.Length == 0) {
                continue;
            }
            result.Add(new SpawnGroupEntry(groupId, count, Math.Max(weights.GetValueOrDefault(groupId), 0), npcIds));
        }
        return result;
    }
}

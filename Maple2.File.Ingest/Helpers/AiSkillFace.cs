using System.Xml;
using Maple2.File.Parser.Xml.AI;

namespace Maple2.File.Ingest.Helpers;

// Whether each AI skill node writes faceTarget. The parser reads a node without the attribute as 0, the same as a
// written faceTarget="0", but the two differ: a skill node without the attribute turns the npc to its target at the
// cast, one with faceTarget="0" does not (KMS2 sends both: a boss whose AI writes no faceTarget on its skill nodes and
// whose motions have faceTarget="0" turns to its target at every cast, also right round, and one whose nodes write
// faceTarget="0" keeps its facing). So a node without the attribute gets 1. The AI files write the attribute on
// 12675 skill nodes (0, 1 and 3) and leave it out on 19730.
public static class AiSkillFace {
    public const int Unwritten = 1;

    private static readonly string[] Sections = ["npcAi/reserved", "npcAi/battle", "npcAi/battleEnd", "npcAi/aiPresets"];

    /// <summary>
    /// For each skill node of the (sanitized) AI file, in the parser's order (section by section, each tree parent
    /// first), whether it writes faceTarget.
    /// </summary>
    public static List<bool> Written(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        var written = new List<bool>();
        foreach (string section in Sections) {
            XmlNode? root = document.SelectSingleNode(section);
            if (root != null) {
                Collect(root, written);
            }
        }
        return written;
    }

    private static void Collect(XmlNode root, List<bool> written) {
        foreach (XmlElement child in root.ChildNodes.OfType<XmlElement>()) {
            if (child.Name == "node" && child.GetAttribute("name").ToLower() == "skill") {
                written.Add(child.HasAttribute("faceTarget"));
            }
            Collect(child, written);
        }
    }

    /// <summary>The parsed file's skill nodes in the same order.</summary>
    public static List<SkillNode> Nodes(NpcAi data) {
        var nodes = new List<SkillNode>();
        foreach (List<Entry> section in new[] { data.Reserved, data.Battle, data.BattleEnd, data.AiPresets }) {
            Collect(section, nodes);
        }
        return nodes;
    }

    private static void Collect(IEnumerable<Entry> entries, List<SkillNode> nodes) {
        foreach (Entry entry in entries) {
            if (entry is SkillNode skill) {
                nodes.Add(skill);
            }
            Collect(entry.Entries, nodes);
        }
    }

    /// <summary>The skill nodes of the parsed file that do not write faceTarget; null when the two walks differ.</summary>
    public static HashSet<SkillNode>? UnwrittenNodes(NpcAi data, string xml) {
        List<bool> written = Written(xml);
        List<SkillNode> nodes = Nodes(data);
        if (written.Count != nodes.Count) {
            return null;
        }
        var unwritten = new HashSet<SkillNode>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < nodes.Count; i++) {
            if (!written[i]) {
                unwritten.Add(nodes[i]);
            }
        }
        return unwritten;
    }
}

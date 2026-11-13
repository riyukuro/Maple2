using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;

namespace Maple2.File.Ingest.Helpers;

// The target order of each skill attack (attack direction), which the parser's attack does not carry. The client reads
// it with the attack (SkillAttackData, 3 when not given) and sorts the targets its search finds by it before the target
// count is taken (CBaseCollision::Detect). Read from the same skill files as the parser, by skill id, level, the
// motion's place in the level and the attack's place in the motion; a level row that is not available for the locale
// and features is skipped, as in SkillMoveTypes.
public sealed class SkillAttackDirections {
    public const int Default = 3;

    private readonly Dictionary<(int Skill, short Level, int Motion, int Attack), int> directions = new();

    // Attacks with a direction other than the default, and keys two available rows give different directions for (the
    // first row's is kept).
    public int Count => directions.Count;
    public int Conflicts { get; private set; }

    public SkillAttackDirections(M2dReader xmlReader) {
        var seen = new HashSet<(int, short, int, int)>();
        foreach (PackFileEntry entry in xmlReader.Files.Where(file => file.Name.StartsWith("skill/") && file.Name.EndsWith(".xml"))) {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out int skillId)) {
                continue;
            }
            XmlNodeList? levels = xmlReader.GetXmlDocument(entry).SelectNodes("//level");
            if (levels == null) {
                continue;
            }
            foreach (XmlElement level in levels.OfType<XmlElement>()) {
                if (!ClientListedRows.Available(level) || !short.TryParse(level.GetAttribute("value"), out short value)) {
                    continue;
                }
                int motionIndex = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "motion")) {
                    int attackIndex = 0;
                    foreach (XmlElement attack in motion.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "attack")) {
                        Add(seen, (skillId, value, motionIndex, attackIndex++), attack);
                    }
                    motionIndex++;
                }
            }
        }
    }

    private void Add(HashSet<(int, short, int, int)> seen, (int, short, int, int) key, XmlElement attack) {
        int direction = int.TryParse(attack.GetAttribute("direction"), out int given) ? given : Default;
        if (!seen.Add(key)) {
            if ((Get(key.Item1, key.Item2, key.Item3, key.Item4) ?? Default) != direction) {
                Conflicts++;
            }
            return;
        }
        if (direction != Default) {
            directions[key] = direction;
        }
    }

    // Null for the default, so the metadata keeps only the other orders.
    public int? Get(int skillId, short level, int motion, int attack) =>
        directions.TryGetValue((skillId, level, motion, attack), out int direction) ? direction : null;
}

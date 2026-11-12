using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;

namespace Maple2.File.Ingest.Helpers;

// The move type of each skill motion (motionProperty movetype), which the parser's motion property does not carry.
// The client reads it with the motion (SkillMotionData, commonfiles/skillleveldata.cpp:1190) and plays a motion of
// type 3 at its own speed whatever the attack speed. Read from the same skill files as the parser, by skill id, level
// and the motion's place in the level; a level row that is not available for the locale and features is skipped.
public sealed class SkillMoveTypes {
    private readonly Dictionary<(int Skill, short Level, int Motion), int> types = new();

    // Motions with a move type, and (skill, level, motion) keys two available rows give different types for (the
    // first row's is kept).
    public int Count => types.Count;
    public int Conflicts { get; private set; }

    public SkillMoveTypes(M2dReader xmlReader) {
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
                int index = 0;
                foreach (XmlElement motion in level.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "motion")) {
                    Add(skillId, value, index++, motion);
                }
            }
        }
    }

    private void Add(int skillId, short level, int index, XmlElement motion) {
        XmlElement? property = motion.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.Name == "motionProperty");
        if (property == null || !int.TryParse(property.GetAttribute("movetype"), out int moveType) || moveType == 0) {
            return;
        }
        if (types.TryGetValue((skillId, level, index), out int known)) {
            if (known != moveType) {
                Conflicts++;
            }
            return;
        }
        types[(skillId, level, index)] = moveType;
    }

    public int Get(int skillId, short level, int motion) => types.GetValueOrDefault((skillId, level, motion));
}

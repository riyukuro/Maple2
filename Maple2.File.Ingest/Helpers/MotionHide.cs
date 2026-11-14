using System.Xml;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;

namespace Maple2.File.Ingest.Helpers;

// The additional effect levels whose <MotionProperty hide> is set, which the parser does not carry. The client hides
// the holder of such an enabled effect (AdditionalCliList::FUN_140972a00 sets m_motion.m_nHide), and its target
// filter (FilterTarget, basecollision.cpp:784) takes a hidden actor only for a range whose applyTarget has bit 4.
// Read from the same files as the parser, by effect id and level; a level row that is not available for the locale
// and features is skipped, and the first available row of a level counts, as in CooldownGroupRates.
public sealed class MotionHide {
    private readonly HashSet<(int Id, short Level)> hidden = [];
    private readonly HashSet<(int Id, short Level)> seen = [];

    public int Count => hidden.Count;

    public MotionHide(M2dReader xmlReader) {
        foreach (PackFileEntry entry in xmlReader.Files.Where(file => file.Name.StartsWith("additionaleffect/") && file.Name.EndsWith(".xml"))) {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out int id)) {
                continue;
            }
            XmlNodeList? levels = xmlReader.GetXmlDocument(entry).SelectNodes("//level");
            if (levels == null) {
                continue;
            }
            foreach (XmlElement level in levels.OfType<XmlElement>()) {
                if (ClientListedRows.Available(level)) {
                    Add(id, level);
                }
            }
        }
    }

    private void Add(int id, XmlElement level) {
        XmlElement? basic = level.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.Name == "BasicProperty");
        if (basic == null || !short.TryParse(basic.GetAttribute("level"), out short value) || !seen.Add((id, value))) {
            return;
        }
        if (level.ChildNodes.OfType<XmlElement>().Any(node => node.Name == "MotionProperty" && Read(node.GetAttribute("hide")))) {
            hidden.Add((id, value));
        }
    }

    // The client reads the attribute as an int (XmlGetAttr) and tests it against 0.
    public static bool Read(string value) => int.TryParse(value, out int hide) && hide != 0;

    public bool Get(int id, short level) => hidden.Contains((id, level));
}

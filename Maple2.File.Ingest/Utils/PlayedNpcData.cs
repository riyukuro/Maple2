using System.Xml;
using System.Xml.Serialization;
using Maple2.File.Parser.Xml.Npc;

namespace Maple2.File.Ingest.Utils;

/// <summary>
/// The NpcData of an NPC that only the played client has, read from that client's npcdata/&lt;nnn&gt;.xml. That file
/// keeps the old npc/&lt;id&gt;.xml sections as children of the &lt;environment&gt;, but the &lt;basic&gt;, &lt;exp&gt;
/// and &lt;lookattarget&gt; values are attributes of the &lt;environment&gt; itself and the speeds are attributes of
/// &lt;model&gt; (CNpcData::LoadXml, commonfiles/npcdata.cpp:1440-1530). The element is rebuilt in the old layout and
/// read with the parser's NpcData type.
/// The played file leaves out an attribute that holds the client's default, and the parser's default differs from
/// the client's for some of them, so each one the server reads is filled with the client's value when it is missing:
/// CNpcData() (npcdata.cpp:647) has kind 0, level 1, class 1, customExp -1, lookAtMyPCWhenTalking and useTalkMotion 1
/// and speeds 0; Unk_NpcDead() (npcdata.cpp:410) time, revival, count, lifeTime and extendRoomTime 0. LoadXml sets
/// movearea 200 when &lt;normal&gt; has none (npcdata.cpp:1685, XmlGetAttr is nonzero for a missing attribute) and
/// Idle_A with movearea 200 when there is no &lt;normal&gt; (npcdata.cpp:1688), as the parser's Normal does. For &lt;distance&gt; the values are the ones the data folder's pack
/// holds wherever the played file leaves the attribute out (sightHeightUP 200, sightHeightDown 50, the rest 0).
/// </summary>
public static class PlayedNpcData {
    private static readonly XmlSerializer Serializer = new(typeof(NpcData), new XmlRootAttribute("environment") { Namespace = "" });

    // The environment attributes that are not the old <basic>'s.
    private static readonly HashSet<string> NotBasic = ["feature", "locale", "customExp", "lookAtMyPCWhenTalking", "useTalkMotion"];

    private static readonly (string Element, string Attribute, string Value)[] Defaults = [
        ("basic", "kind", "0"),
        ("basic", "level", "1"),
        ("basic", "class", "1"),
        ("exp", "customExp", "-1"),
        ("lookattarget", "lookAtMyPCWhenTalking", "1"),
        ("lookattarget", "useTalkMotion", "1"),
        ("speed", "rotation", "0"),
        ("speed", "walk", "0"),
        ("speed", "run", "0"),
        ("dead", "time", "0"),
        ("dead", "revival", "0"),
        ("dead", "count", "0"),
        ("dead", "lifeTime", "0"),
        ("dead", "extendRoomTime", "0"),
        ("normal", "movearea", "200"),
        ("distance", "avoid", "0"),
        ("distance", "sight", "0"),
        ("distance", "sightHeightUP", "200"),
        ("distance", "sightHeightDown", "50"),
        ("distance", "customLastSightRadius", "0"),
        ("distance", "customLastSightHeightUp", "0"),
        ("distance", "customLastSightHeightDown", "0"),
    ];

    /// <param name="environment">The &lt;environment&gt; of the played client's &lt;npc&gt; that the client keeps
    /// (PlayedNpcStats.SelectEnvironment).</param>
    public static NpcData Read(XmlElement environment) {
        var document = new XmlDocument();
        XmlElement root = document.CreateElement("environment");
        document.AppendChild(root);

        XmlElement basic = document.CreateElement("basic");
        XmlElement exp = document.CreateElement("exp");
        XmlElement lookAt = document.CreateElement("lookattarget");
        foreach (XmlAttribute attribute in environment.Attributes) {
            switch (attribute.Name) {
                case "customExp":
                    exp.SetAttribute(attribute.Name, attribute.Value);
                    break;
                case "lookAtMyPCWhenTalking":
                case "useTalkMotion":
                    lookAt.SetAttribute(attribute.Name, attribute.Value);
                    break;
            }
            if (!NotBasic.Contains(attribute.Name)) {
                basic.SetAttribute(attribute.Name, attribute.Value);
            }
        }
        root.AppendChild(basic);
        root.AppendChild(exp);
        root.AppendChild(lookAt);

        XmlElement speed = document.CreateElement("speed");
        foreach (XmlElement child in environment.ChildNodes.OfType<XmlElement>()) {
            if (child.Name is "basic" or "exp" or "lookattarget" or "speed" or "effectdummy") {
                continue;
            }
            root.AppendChild(document.ImportNode(child, true));
            if (child.Name == "model") {
                CopyAttribute(child, "rotationSpeed", speed, "rotation");
                CopyAttribute(child, "walkSpeed", speed, "walk");
                CopyAttribute(child, "runSpeed", speed, "run");
            }
        }
        root.AppendChild(speed);

        foreach ((string name, string attribute, string value) in Defaults) {
            XmlElement? element = root[name];
            if (element == null) {
                element = document.CreateElement(name);
                root.AppendChild(element);
            }
            if (!element.HasAttribute(attribute)) {
                element.SetAttribute(attribute, value);
            }
        }

        using var reader = new XmlNodeReader(root);
        var data = (NpcData) Serializer.Deserialize(reader)!;
        // The AI file is the server's alone, and the played file names none where the server's pack holds its default.
        // That pack has none for a friendly="2" NPC (3885 of the 3986 such NPCs the two packs share) and
        // AI_DefaultNew.xml for any other (3165 of 3515).
        if (string.IsNullOrEmpty(data.aiInfo.path) && data.basic.friendly != 2) {
            data.aiInfo.path = DefaultAi;
        }
        return data;
    }

    public const string DefaultAi = "AI_DefaultNew.xml";

    private static void CopyAttribute(XmlElement from, string fromName, XmlElement to, string toName) {
        if (from.HasAttribute(fromName)) {
            to.SetAttribute(toName, from.GetAttribute(fromName));
        }
    }
}

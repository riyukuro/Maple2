using System.Xml;

namespace Maple2.File.Ingest.Utils;

/// <summary>
/// The skills a player's client can send in a cast, by the client's rule. Every cast record the client sends
/// comes from CSkillSystem::CreateSkillRecord (client/ms2/skillslot.cpp:2140) or from the quick slot path before
/// it (FUN_141764e30, :2084), and the skill must pass JobSystemCli::vfunc_2 (client/ms2/jobsystemcli.cpp:80):
/// a skill of a main type that needs no learning (SkillData::FUN_1407e9f30, commonfiles/skilloverlapcompare.cpp:1111)
/// passes; any other must be in the skill set the player holds (a ride's, a transformation effect's skillSetID),
/// in the skill tree, a lapenshard's skill (the equipped item's skill id, MyPC mypc.cpp:4311) or the sub skill of
/// a tree skill. The other senders are item skills (inventory, the bound item box, the pet's item), the constants
/// the client casts by id (hold attack, pet summon, return home, one shot) and the skills a cast turns into: the
/// combo's output skill, the change skill, the jump (kinds jump, GetCastSkillId) and the default skill of
/// stateAttr useDefaultSkillID. Emotions (main types 14 and 15) need no learning. A skill none of these reach is
/// cast only by the server (condition skills, effect event and splash skills, NPC and boss skills).
/// </summary>
public static class PlayerSkillSources {
    // SkillData::FUN_1407e9f30 returns false for these main types: the skill needs no learning.
    public static readonly HashSet<int> NoLearnMainTypes = [1, 3, 4, 5, 7, 10, 11, 13, 14, 15, 17, 18, 19];

    // The constants.xml keys whose value is a skill the client casts by id.
    public static readonly string[] SkillConstants = ["HoldAttackSkillID", "SummonPetSkillID", "returnHomeSkill", "returnHomeSkillMerat", "OneShotSkillID"];

    /// <summary>
    /// The main and sub skills of every job of a job.xml: the data pack's &lt;skill main=".." sub=".."/&gt; and the
    /// played client's &lt;skills&gt;&lt;v skillID=".." subSkillIDs=".."/&gt;, the row JobSkillData::Load reads
    /// (commonfiles/jobtable.cpp:99).
    /// </summary>
    public static IEnumerable<int> TreeSkills(XmlDocument job) {
        foreach ((string path, string main, string sub) in new[] { ("//skill", "main", "sub"), ("//skills/v", "skillID", "subSkillIDs") }) {
            XmlNodeList? nodes = job.SelectNodes(path);
            if (nodes == null) {
                continue;
            }
            foreach (XmlElement skill in nodes.OfType<XmlElement>()) {
                foreach (int id in Ids(skill.GetAttribute(main)).Concat(Ids(skill.GetAttribute(sub)))) {
                    yield return id;
                }
            }
        }
    }

    /// <summary>The skills of every skill set of a skillset.xml (&lt;skillSet skillIDs="a, b"/&gt;).</summary>
    public static IEnumerable<int> SkillSetSkills(XmlDocument skillSet) {
        XmlNodeList? nodes = skillSet.SelectNodes("//skillSet");
        if (nodes == null) {
            yield break;
        }
        foreach (XmlElement set in nodes.OfType<XmlElement>()) {
            foreach (int id in Ids(set.GetAttribute("skillIDs"))) {
                yield return id;
            }
        }
    }

    /// <summary>The values of the skill constants of a constants.xml, whatever their feature or locale.</summary>
    public static IEnumerable<int> ConstantSkills(XmlDocument constants) {
        XmlNodeList? nodes = constants.SelectNodes("//v");
        if (nodes == null) {
            yield break;
        }
        foreach (XmlElement v in nodes.OfType<XmlElement>()) {
            if (SkillConstants.Contains(v.GetAttribute("key")) && int.TryParse(v.GetAttribute("value"), out int id) && id != 0) {
                yield return id;
            }
        }
    }

    /// <summary>The skills a cast of a played client's skill element turns into, any level.</summary>
    public static IEnumerable<int> Edges(XmlElement skill) {
        foreach ((string element, string attribute) in new[] { ("combo", "outputSkill"), ("changeSkill", "changeSkillID"), ("stateAttr", "useDefaultSkillID"), ("kinds", "jump") }) {
            XmlNodeList? nodes = skill.SelectNodes($".//{element}");
            if (nodes == null) {
                continue;
            }
            foreach (XmlElement node in nodes.OfType<XmlElement>()) {
                foreach (int id in Ids(node.GetAttribute(attribute))) {
                    yield return id;
                }
            }
        }
    }

    /// <summary>
    /// The skills a player can cast: those of a main type that needs no learning, the granted ones, and every skill
    /// one of them turns into.
    /// </summary>
    public static HashSet<int> Castable(IReadOnlyDictionary<int, int> mainTypes, IReadOnlyDictionary<int, HashSet<int>> edges, IEnumerable<int> granted) {
        var castable = new HashSet<int>();
        var todo = new Stack<int>();
        foreach (int id in mainTypes.Where(entry => NoLearnMainTypes.Contains(entry.Value)).Select(entry => entry.Key).Concat(granted)) {
            if (castable.Add(id)) {
                todo.Push(id);
            }
        }
        while (todo.Count > 0) {
            if (!edges.TryGetValue(todo.Pop(), out HashSet<int>? next)) {
                continue;
            }
            foreach (int id in next) {
                if (castable.Add(id)) {
                    todo.Push(id);
                }
            }
        }
        return castable;
    }

    private static IEnumerable<int> Ids(string value) {
        if (string.IsNullOrEmpty(value)) {
            yield break;
        }
        foreach (string part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            if (int.TryParse(part, out int id) && id != 0) {
                yield return id;
            }
        }
    }
}

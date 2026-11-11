using System.Xml;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// The talk choices (distractors) of the played client's own scripts. A pick the client sends (NpcTalk mode 2, an
// index from CSelectableDistractorCinematicComponent::vfunc_1, client/ms2/cinematiccomponent.cpp:721) counts the
// choices of its own script content in the order it shows them (vfunc_5, cinematiccomponent.cpp:725), so a data
// folder of another version with the choices in another order answers each pick with another choice's goto.
//
// The client reads every Script/Npc file whose name starts with npcscript and every Script/Quest file whose name
// starts with questscript (LoadTalkScripts, commonfiles/talkscript.cpp). Under an <npc>, the <script>, <select> and
// <job> elements are taken one per id among those that pass the feature and locale test (FUN_14028faa0,
// commonfiles/commonutil.cpp:1747); a <script> with randomPick 1 and no content is not kept (LoadNpcTalks,
// talkscript.cpp:708). Under a <quest> with a non-zero id, its <script> elements the same way (LoadQuestTalks). Each
// <content> keeps its <distractor> children in document order (CCinematicContent::vfunc_2,
// commonfiles/scriptcontent.cpp:271). After loading, a choice of an npc <script> or a quest <script> is dropped when
// its goto names ids and none of them is a kept script of that npc or quest (CCinematicContent::vfunc_3 and vfunc_4,
// scriptcontent.cpp:301 and :333, run by FUN_1408559b0, talkscript.cpp:783); <select> and <job> keep theirs.
public static class PlayedClientScripts {
    public const string NpcFolder = "script/npc/";
    public const string QuestFolder = "script/quest/";

    public readonly record struct Key(ScriptType Type, int Id, int State, int Content);

    public static bool IsNpcFile(string name) => IsFile(name, NpcFolder, "npcscript");
    public static bool IsQuestFile(string name) => IsFile(name, QuestFolder, "questscript");

    private static bool IsFile(string name, string folder, string prefix) {
        if (!name.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        string file = name[folder.Length..];
        return !file.Contains('/') && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    }

    // The choices of each content of the played client's scripts. available is the feature and locale test.
    public static Dictionary<Key, CinematicDistractor[]> Distractors(IEnumerable<XmlDocument> npcFiles, IEnumerable<XmlDocument> questFiles,
            Func<XmlElement, bool> available) {
        var result = new Dictionary<Key, CinematicDistractor[]>();
        foreach (XmlDocument document in npcFiles) {
            foreach (XmlElement npc in Children(document.DocumentElement, "npc")) {
                int npcId = Int(npc, "id");
                Dictionary<int, XmlElement> scripts = PickById(npc, "script", available)
                    .Where(entry => !(Int(entry.Value, "randomPick") == 1 && !Children(entry.Value, "content").Any()))
                    .ToDictionary(entry => entry.Key, entry => entry.Value);
                AddContents(result, ScriptType.Npc, npcId, scripts, scripts.Keys.ToHashSet());
                AddContents(result, ScriptType.Npc, npcId, PickById(npc, "select", available).Take(1), null);
                AddContents(result, ScriptType.Npc, npcId, PickById(npc, "job", available).Take(1), null);
            }
        }
        foreach (XmlDocument document in questFiles) {
            foreach (XmlElement quest in Children(document.DocumentElement, "quest")) {
                int questId = Int(quest, "id");
                if (questId == 0) {
                    continue;
                }
                Dictionary<int, XmlElement> scripts = PickById(quest, "script", available).ToDictionary(entry => entry.Key, entry => entry.Value);
                AddContents(result, ScriptType.Quest, questId, scripts, scripts.Keys.ToHashSet());
            }
        }
        return result;
    }

    private static void AddContents(Dictionary<Key, CinematicDistractor[]> result, ScriptType type, int id,
            IEnumerable<KeyValuePair<int, XmlElement>> scripts, HashSet<int>? targets) {
        foreach ((int stateId, XmlElement script) in scripts) {
            int index = 0;
            foreach (XmlElement content in Children(script, "content")) {
                var choices = new List<CinematicDistractor>();
                foreach (XmlElement choice in Children(content, "distractor")) {
                    int[] @goto = Ids(choice.GetAttribute("goto"));
                    if (targets != null && @goto.Length > 0 && !@goto.Any(targets.Contains)) {
                        continue;
                    }
                    choices.Add(new CinematicDistractor(@goto, Ids(choice.GetAttribute("gotoFail"))));
                }
                result[new Key(type, id, stateId, index++)] = choices.ToArray();
            }
        }
    }

    // The children named name that pass, one per id in document order; for an id seen again the first is kept.
    private static IEnumerable<KeyValuePair<int, XmlElement>> PickById(XmlElement parent, string name, Func<XmlElement, bool> available) {
        var picked = new Dictionary<int, XmlElement>();
        var order = new List<int>();
        foreach (XmlElement element in Children(parent, name)) {
            if (!available(element)) {
                continue;
            }
            int id = Int(element, "id");
            if (picked.TryAdd(id, element)) {
                order.Add(id);
            }
        }
        return order.Select(id => new KeyValuePair<int, XmlElement>(id, picked[id]));
    }

    private static IEnumerable<XmlElement> Children(XmlNode? parent, string name) =>
        parent == null ? [] : parent.ChildNodes.OfType<XmlElement>().Where(element => element.Name == name);

    private static int Int(XmlElement element, string attribute) =>
        int.TryParse(element.GetAttribute(attribute), out int value) ? value : 0;

    private static int[] Ids(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out int id) ? id : 0)
            .Where(id => id != 0)
            .ToArray();

    // The state with each content's choices replaced by the played client's for the same script, state and content.
    // Contents the played client lacks keep theirs. changed counts the contents whose choices differ.
    public static CinematicContent[] Apply(IReadOnlyDictionary<Key, CinematicDistractor[]> played, ScriptType type, int id, int stateId,
            CinematicContent[] contents, ref int changed) {
        var result = new CinematicContent[contents.Length];
        for (int i = 0; i < contents.Length; i++) {
            result[i] = contents[i];
            if (!played.TryGetValue(new Key(type, id, stateId, i), out CinematicDistractor[]? choices) || Same(choices, contents[i].Distractors)) {
                continue;
            }
            result[i] = contents[i] with { Distractors = choices };
            changed++;
        }
        return result;
    }

    private static bool Same(CinematicDistractor[] a, CinematicDistractor[] b) =>
        a.Length == b.Length && a.Zip(b).All(pair => pair.First.Goto.SequenceEqual(pair.Second.Goto) && pair.First.GotoFail.SequenceEqual(pair.Second.GotoFail));
}

using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: a talk pick is an index into the played client's own choices of the script content.
public class PlayedClientScriptsTests {
    private static XmlDocument File(string body) {
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{body}</ms2>");
        return document;
    }

    private static bool Available(XmlElement node) => node.GetAttribute("feature") != "Off" && node.GetAttribute("locale") is "" or "KR";

    private static CinematicDistractor Choice(int[] @goto, int[] gotoFail) => new(@goto, gotoFail);

    private static CinematicContent Content(params CinematicDistractor[] choices) => new("", NpcTalkButton.None, 0, choices, []);

    private static string Gotos(CinematicDistractor[] choices) =>
        string.Join(" | ", choices.Select(choice => string.Join(",", choice.Goto) + "/" + string.Join(",", choice.GotoFail)));

    // The played client lists the second choice of the data folder's content first: pick 0 is that choice.
    private const string Swapped = """
        <npc id="900501">
            <script id="20"><content text="a">
                <distractor text="b" goto="40" gotoFail="50" />
                <distractor text="c" goto="30" />
            </content></script>
            <script id="30"><content text="d" /></script>
            <script id="40"><content text="e" /></script>
            <script id="50"><content text="f" /></script>
        </npc>
        """;

    [Test]
    public void Apply_PickIndexesThePlayedClientsChoices() {
        Dictionary<PlayedClientScripts.Key, CinematicDistractor[]> played = PlayedClientScripts.Distractors([File(Swapped)], [], Available);
        CinematicContent[] data = [Content(Choice([30], []), Choice([40], [50]))];

        int changed = 0;
        CinematicContent[] contents = PlayedClientScripts.Apply(played, ScriptType.Npc, 900501, 20, data, ref changed);

        Assert.That(changed, Is.EqualTo(1));
        Assert.That(contents[0].Distractors.ElementAtOrDefault(0)?.Goto, Is.EqualTo(new[] { 40 })); // pick 0
        Assert.That(contents[0].Distractors.ElementAtOrDefault(0)?.GotoFail, Is.EqualTo(new[] { 50 }));
        Assert.That(contents[0].Distractors.ElementAtOrDefault(1)?.Goto, Is.EqualTo(new[] { 30 })); // pick 1
        Assert.That(contents[0].Distractors.ElementAtOrDefault(2), Is.Null);
    }

    [Test]
    public void Apply_LeavesContentsTheSameOrNotInThePlayedClient() {
        Dictionary<PlayedClientScripts.Key, CinematicDistractor[]> played = PlayedClientScripts.Distractors([File(Swapped)], [], Available);
        CinematicContent same = Content(Choice([40], [50]), Choice([30], []));
        CinematicContent extra = Content(Choice([30], []));

        int changed = 0;
        CinematicContent[] contents = PlayedClientScripts.Apply(played, ScriptType.Npc, 900501, 20, [same, extra], ref changed);
        CinematicContent[] other = PlayedClientScripts.Apply(played, ScriptType.Npc, 900502, 20, [extra], ref changed);
        CinematicContent[] quest = PlayedClientScripts.Apply(played, ScriptType.Quest, 900501, 20, [extra], ref changed);

        Assert.That(changed, Is.EqualTo(0));
        Assert.That(contents[0], Is.SameAs(same));
        Assert.That(contents[1], Is.SameAs(extra)); // the played content has one content only
        Assert.That(other[0], Is.SameAs(extra));
        Assert.That(quest[0], Is.SameAs(extra));
    }

    // A choice of an npc <script> whose goto names no kept script of the npc is dropped, so later picks move up; a
    // randomPick script without content is not kept. <select> keeps every choice.
    [Test]
    public void Distractors_DropAChoiceToNoScript() {
        XmlDocument document = File("""
            <npc id="900503">
                <select id="0"><content text="a"><distractor text="b" goto="99" /></content></select>
                <script id="10"><content text="c">
                    <distractor text="d" goto="99" />
                    <distractor text="e" goto="70" />
                    <distractor text="f" goto="99,30" gotoFail="99" />
                    <distractor text="g" />
                </content></script>
                <script id="30"><content text="h" /></script>
                <script id="70" randomPick="1" />
            </npc>
            """);

        Dictionary<PlayedClientScripts.Key, CinematicDistractor[]> played = PlayedClientScripts.Distractors([document], [], Available);

        Assert.That(Gotos(played[new PlayedClientScripts.Key(ScriptType.Npc, 900503, 10, 0)]), Is.EqualTo("99,30/99 | /"));
        Assert.That(Gotos(played[new PlayedClientScripts.Key(ScriptType.Npc, 900503, 0, 0)]), Is.EqualTo("99/"));
        Assert.That(played.ContainsKey(new PlayedClientScripts.Key(ScriptType.Npc, 900503, 70, 0)), Is.False);
    }

    // One element per id among those that pass the test, the first kept; a quest's choices go to its own scripts.
    [Test]
    public void Distractors_TakeTheAvailableElementsAndQuests() {
        XmlDocument npcs = File("""
            <npc id="900504">
                <script id="20" feature="Off"><content text="a"><distractor text="b" goto="30" /></content></script>
                <script id="20" locale="KR"><content text="a"><distractor text="c" goto="40" /><distractor text="d" goto="30" /></content></script>
                <script id="20"><content text="a"><distractor text="e" goto="30" /></content></script>
                <script id="30"><content text="f" /></script>
                <script id="40" locale="NA"><content text="g" /></script>
            </npc>
            """);
        XmlDocument quests = File("""
            <quest id="900505">
                <script id="100"><content text="a"><distractor text="b" goto="101" /><distractor text="c" goto="30" /></content></script>
                <script id="101"><content text="d" /></script>
            </quest>
            <quest id="0">
                <script id="100"><content text="a"><distractor text="b" goto="101" /></content></script>
            </quest>
            """);

        Dictionary<PlayedClientScripts.Key, CinematicDistractor[]> played = PlayedClientScripts.Distractors([npcs], [quests], Available);

        // the KR element; its goto 40 names a script that is not available, so only goto 30 is left
        Assert.That(Gotos(played[new PlayedClientScripts.Key(ScriptType.Npc, 900504, 20, 0)]), Is.EqualTo("30/"));
        Assert.That(Gotos(played[new PlayedClientScripts.Key(ScriptType.Quest, 900505, 100, 0)]), Is.EqualTo("101/"));
        Assert.That(played.Keys.Any(key => key.Id == 0), Is.False);
    }

    [TestCase("script/npc/npcscript_final.xml", true, false)]
    [TestCase("Script/Npc/NpcScript_extra.xml", true, false)]
    [TestCase("script/npc/11004288.xml", false, false)]
    [TestCase("script/npc/sub/npcscript_final.xml", false, false)]
    [TestCase("script/quest/questscript_final.xml", false, true)]
    [TestCase("string/en/npcscript_final.xml", false, false)]
    public void ScriptFiles(string name, bool npc, bool quest) {
        Assert.Multiple(() => {
            Assert.That(PlayedClientScripts.IsNpcFile(name), Is.EqualTo(npc));
            Assert.That(PlayedClientScripts.IsQuestFile(name), Is.EqualTo(quest));
        });
    }
}

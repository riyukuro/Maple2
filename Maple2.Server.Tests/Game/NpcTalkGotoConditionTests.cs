using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// An npc <script> with gotoConditionTalkID: the client gives its last page Next (talkscript.cpp:877: isLast needs an
// empty id list) and sends NpcTalk 2 with pick 0 for it; the talk goes on to the first listed script whose
// npcScriptCondition row passes (maid 11000512 script 11 lists 40, 50, 60, and 40 and 50 can both pass).
// The npc and script ids are made up; the conditions are job tests (the test talker is a Newbie).
public class NpcTalkGotoConditionTests {
    private const int NpcId = 11000001;

    private TestGameSession test = null!;

    private static ScriptState State(int id, int pages, params int[] gotoIds) =>
        new(id, ScriptStateType.Script, false, null,
            Enumerable.Range(0, pages).Select(_ => new CinematicContent("", NpcTalkButton.None, 0, [], [])).ToArray(), gotoIds);

    private static ScriptConditionMetadata Job(int scriptId, JobCode job) =>
        new(NpcId, scriptId, ScriptType.Npc, null!, null!, [job], new Dictionary<int, bool>(), new Dictionary<int, bool>(), [],
            default, default, default, default, false, false);

    // Talks script 10 (two pages, goto 11, 12, 13) on its first page; 11 needs a Knight, 12 and 13 a Newbie.
    private NpcScriptManager Talk(params ScriptConditionMetadata[] conditions) => TalkWith([11, 12, 13], conditions);

    private NpcScriptManager TalkWith(int[] gotoIds, params ScriptConditionMetadata[] conditions) {
        test = new TestGameSession();
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>> {
            [NpcId] = conditions.ToDictionary(condition => condition.ScriptId),
        }));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));
        GameSession session = test.Start();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(NpcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));

        ScriptState start = State(10, 2, gotoIds);
        var script = new ScriptMetadata(NpcId, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [10] = start, [11] = State(11, 1), [12] = State(12, 1), [13] = State(13, 1),
        });
        session.NpcScript = new NpcScriptManager(session, npc, script, start, NpcTalkType.Talk);
        test.TakeSent();
        return session.NpcScript;
    }

    [Test]
    public void TheLastPage_HasNext_AndGoesToTheFirstPassingScript() {
        NpcScriptManager talk = Talk(Job(11, JobCode.Knight), Job(12, JobCode.Newbie), Job(13, JobCode.Newbie));
        Assert.That(talk.Continue(0), Is.True);
        Assert.That(talk.Button, Is.EqualTo(NpcTalkButton.Next));
        Assert.That(talk.Continue(0), Is.True);
        Assert.Multiple(() => {
            Assert.That(talk.State!.Id, Is.EqualTo(12));
            Assert.That(talk.Index, Is.EqualTo(0));
        });
    }

    [Test]
    public void AScriptWithoutConditionRow_Passes() {
        NpcScriptManager talk = Talk(Job(12, JobCode.Newbie));
        talk.Continue(0);
        talk.Continue(0);
        Assert.That(talk.State!.Id, Is.EqualTo(11));
    }

    [Test]
    public void NoPassingScript_ClosesTheTalk() {
        NpcScriptManager talk = Talk(Job(11, JobCode.Knight), Job(12, JobCode.Knight), Job(13, JobCode.Knight));
        talk.Continue(0);
        Assert.That(talk.Continue(0), Is.False);
        Assert.That(test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.NpcTalk), Is.EqualTo(2));
    }

    // Script 11 has no list: its one page is the last, with no Next, and Continue closes.
    [Test]
    public void AScriptWithoutTheList_EndsOnItsLastPage() {
        NpcScriptManager talk = Talk();
        talk.Continue(0);
        talk.Continue(0);
        Assert.That(talk.State!.Id, Is.EqualTo(11));
        Assert.That(talk.Button, Is.Not.EqualTo(NpcTalkButton.Next));
        Assert.That(talk.Continue(0), Is.False);
    }

    // A script listing its own id skips it (it would restart on its last page) and goes on to the next one.
    [Test]
    public void TheScriptsOwnId_IsSkipped() {
        NpcScriptManager talk = TalkWith([10, 12]);
        talk.Continue(0);
        Assert.That(talk.Continue(0), Is.True);
        Assert.That(talk.State!.Id, Is.EqualTo(12));
    }
}

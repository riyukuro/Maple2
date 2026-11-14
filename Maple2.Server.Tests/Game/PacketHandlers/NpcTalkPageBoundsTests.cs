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
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// NpcTalk 2 on a choice page: the client sends the index it is given on unchecked (cinematiccomponent.cpp:721 and
// :781 call SendNpcTalkMode2(nSelect)), so a pick past the page's choices is possible. It closes the talk without
// running the page's end function first. The npc, script and item ids are made up.
public class NpcTalkPageBoundsTests {
    private const int NpcId = 11900002, Present = 30000145;

    private TestGameSession test = null!;

    private static CinematicContent Page(int choices) => new("", NpcTalkButton.SelectableTalk, 1,
        Enumerable.Range(0, choices).Select(_ => new CinematicDistractor([20], [])).ToArray(), []);

    // A talk on state 10 whose one page has `choices` choices and an end function giving the present.
    private GameSession Start(int choices) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Present));
        var function = new ScriptFunctionMetadata(NpcId, 10, ScriptType.Npc, 1, true, 0, "", "", "", 0, 0, "", "",
            [new ItemComponent(Present, 1, 1, ItemTag.None)], [], 0, "", "", "", 0, 0, 0, 0, false);
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> {
            [NpcId] = new() { [10] = new() { [1] = function } },
        }));
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        GameSession session = test.Start();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(NpcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));

        var state = new ScriptState(10, ScriptStateType.Script, false, null, [Page(choices)]);
        var script = new ScriptMetadata(NpcId, ScriptType.Npc, new Dictionary<int, ScriptState> { [10] = state });
        session.NpcScript = new NpcScriptManager(session, npc, script, state, NpcTalkType.Talk);
        test.TakeSent();
        return session;
    }

    private List<byte[]> Pick(GameSession session, int pick) {
        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(pick);
        })));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk).ToList();
    }

    private static int Count(GameSession session) => session.Item.Inventory.Find(Present).Sum(item => item.Amount);

    [TestCase(2)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void APickPastTheChoices_ClosesWithoutTheFunction(int pick) {
        GameSession session = Start(choices: 2);
        List<byte[]> sent = Pick(session, pick);
        Assert.Multiple(() => {
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(session.NpcScript, Is.Null);
            Assert.That(Count(session), Is.EqualTo(0));
        });
    }

    [Test]
    public void APickOnThePage_RunsTheFunction() {
        GameSession session = Start(choices: 2);
        Pick(session, 1);
        Assert.That(Count(session), Is.EqualTo(1));
    }

    [Test]
    public void HasPick() {
        GameSession session = Start(choices: 2);
        NpcScriptManager script = session.NpcScript!;
        Assert.Multiple(() => {
            Assert.That(script.HasPick(0), Is.True);
            Assert.That(script.HasPick(1), Is.True);
            Assert.That(script.HasPick(2), Is.False);
            Assert.That(script.HasPick(-1), Is.False);
        });
        GameSession plain = Start(choices: 0);
        Assert.That(plain.NpcScript!.HasPick(5), Is.True);
        plain.NpcScript!.State = null;
        Assert.That(plain.NpcScript!.HasPick(0), Is.False);
    }
}

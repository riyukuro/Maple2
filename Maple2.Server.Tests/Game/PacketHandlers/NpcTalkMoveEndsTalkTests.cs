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

// NpcTalk 2 on a choice page whose end function moves the player to another map: the session clears the talk with its field, so nothing of the page is left to continue. Old code read the cleared talk and threw.
// The npc, script and map ids are made up.
public class NpcTalkMoveEndsTalkTests {
    private const int NpcId = 11900003, From = 2000001, To = 2000002;

    private TestGameSession test = null!;

    private static MapMetadata Field(int id) {
        MapMetadata map = TestGameSession.Map(id);
        return map with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false) };
    }

    private GameSession Start(int moveMapId) {
        test = new TestGameSession();
        var function = new ScriptFunctionMetadata(NpcId, 10, ScriptType.Npc, 1, true, 0, "", "", "", moveMapId, 0, "", "",
            [], [], 0, "", "", "", 0, 0, 0, 0, false);
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> {
            [NpcId] = new() { [10] = new() { [1] = function } },
        }));
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        MapMetadata from = Field(From);
        test.AddMap([from, Field(To)]);
        GameSession session = test.Start();
        test.EnterMovingOut(from);

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(NpcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));

        var page = new CinematicContent("", NpcTalkButton.SelectableTalk, 1,
            Enumerable.Range(0, 2).Select(_ => new CinematicDistractor([20], [])).ToArray(), []);
        var state = new ScriptState(10, ScriptStateType.Script, false, null, [page]);
        var script = new ScriptMetadata(NpcId, ScriptType.Npc, new Dictionary<int, ScriptState> { [10] = state });
        session.NpcScript = new NpcScriptManager(session, npc, script, state, NpcTalkType.Talk | NpcTalkType.Select);
        test.TakeSent();
        return session;
    }

    private void Pick(GameSession session, int pick) {
        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(pick);
        })));
    }

    // The page's function moves the player away: the talk is gone and nothing more is sent for it.
    [Test]
    public void AFunctionThatMovesThePlayerEndsTheTalkQuietly() {
        GameSession session = Start(To);
        // The session leaves its field as the function runs (another thread, or the move's own step): the talk is cleared.
        test.OnSend = _ => session.NpcScript = null;

        Pick(session, 0);

        Assert.Multiple(() => {
            Assert.That(session.NpcScript, Is.Null);
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Has.None.EqualTo(SendOp.NpcTalk));
        });
    }

    // A function that does not move the player continues the talk as before.
    [Test]
    public void AFunctionWithoutAMoveKeepsTheTalk() {
        GameSession session = Start(0);

        Pick(session, 0);

        Assert.Multiple(() => {
            Assert.That(session.NpcScript, Is.Not.Null);
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.NpcTalk));
        });
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An npc's talk row whose script function names a dialog (Natalie 11000327: rows 21 and 22 open the treasure map
// appraise, ItemTreasureMapDialog, the second with "free"): the server sends NpcTalk 3/4 with the dialog name and its
// argument (UITalkManager::FUN_141b1ea90).
public class NpcScriptDialogTests {
    private const int Npc = 11000327;

    private static ScriptState State(int id, ScriptStateType type = ScriptStateType.Script, bool pick = false, int functionId = 0, params int[][] gotos) =>
        new(id, type, pick, null, [new CinematicContent("", NpcTalkButton.None, functionId, gotos.Select(g => new CinematicDistractor(g, [])).ToArray(), [])]);

    private static ScriptFunctionMetadata Function(int scriptId, string uiArg) => new(Npc, scriptId, ScriptType.Npc, 1, false, 0,
        "ItemTreasureMapDialog", uiArg, "", 0, 0, "", "", [], [], 0, "", "", "", 0, 0, 0, 0, false);

    [TestCase(0, "")]
    [TestCase(1, "free")]
    public void AppraiseRow_OpensTheTreasureMapDialog(int pick, string arg) {
        var test = new TestGameSession();
        test.Register(new ScriptMetadata(Npc, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [0] = State(0, ScriptStateType.Select),
            [20] = State(20),
            [21] = State(21, functionId: 1),
            [22] = State(22, functionId: 1),
            [70] = State(70, pick: true, gotos: [[21], [22], [20]]),
        }));
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> {
            [Npc] = new() {
                [21] = new() { [1] = Function(21, "") },
                [22] = new() { [1] = Function(22, "free") },
            },
        }));
        GameSession session = test.Start();
        test.TakeSent();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(Npc, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        typeof(FieldNpc).GetField(nameof(FieldNpc.SpawnAnimation), any)!.SetValue(npc, "spawn");

        NpcTalkHandler.BeginTalk(session, npc);
        Assert.That(session.NpcScript?.State?.Id, Is.EqualTo(70));
        test.TakeSent();

        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(pick);
        }));

        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk).ToList();
        var expected = new ByteWriter();
        expected.WriteByte(3);
        expected.WriteByte(4);
        expected.WriteUnicodeString("ItemTreasureMapDialog");
        expected.WriteUnicodeString(arg);
        Assert.Multiple(() => {
            Assert.That(session.NpcScript?.State?.Id, Is.EqualTo(21 + pick));
            Assert.That(sent.Select(packet => packet.Skip(2).ToArray()), Has.Some.EqualTo(expected.ToArray()));
            Assert.That(session.TreasureMapFree, Is.EqualTo(arg == "free"));
        });
    }
}

using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The merge dialog asks for its result line once the result is shown (UIItemMergeOptionDialog::RequestResultTalk,
// uiitemmergeoptiondialog.cpp:1028): NpcTalk 6 (i32 npc, i32 talk, i16 202, i16 talk id) for a talk without a field npc,
// NpcTalk 4 (i16 202, i16 talk id) else. The smith's line already followed the result (ItemMergeManager), so neither
// answers it; mode 6 used to step the held talk's event list, which threw on an empty list.
public class MergeResultTalkTests {
    private static NpcScriptManager[] Scripts(GameSession session) => [
        new NpcScriptManager(session, []),
        new NpcScriptManager(session, [new CinematicEventScript(1, [new ScriptContent("enchant", "", "")])]),
    ];

    [TestCase(0)]
    [TestCase(1)]
    public void Mode6_SendsNothing(int script) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.NpcScript = Scripts(session)[script];
        test.TakeSent();

        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteInt(11001231);
            writer.WriteInt(1);
            writer.WriteShort(202);
            writer.WriteShort(1);
        })));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk), Is.Empty);
    }

    [Test]
    public void Mode4_SendsNothing() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.NpcScript = Scripts(session)[1];
        test.TakeSent();

        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteShort(202);
            writer.WriteShort(1);
        })));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk), Is.Empty);
    }
}

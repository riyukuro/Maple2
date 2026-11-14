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

// NpcTalk 2 carries the pick the client took from its list (UITalkManager::SendNpcTalkMode2, 0 or a list index).
// A negative pick is not one the client sends; on a select talk it passed the quest check and threw in the list
// lookup, so it is dropped and the talk stays as it was.
public class NpcTalkPickTests {
    private const int NpcId = 11000001;

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void ANegativePick_IsDropped(int pick) {
        var test = new TestGameSession();
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        GameSession session = test.Start();
        test.TakeSent();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(NpcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));

        var script = new NpcScriptManager(session, npc, null, null, NpcTalkType.Select | NpcTalkType.Quest | NpcTalkType.Dialog);
        QuestMetadata quest = TestGameSession.QuestData(10000001);
        script.Quests = new SortedDictionary<int, QuestMetadata> { [quest.Id] = quest };
        session.NpcScript = script;

        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(pick);
        })));
        Assert.Multiple(() => {
            Assert.That(session.NpcScript, Is.SameAs(script));
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk), Is.Empty);
        });
    }
}

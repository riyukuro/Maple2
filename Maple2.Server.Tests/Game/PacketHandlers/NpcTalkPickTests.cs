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
// lookup. It now ends the talk (one close), so a resend finds no talk and cannot run the script's function again.
public class NpcTalkPickTests {
    private const int NpcId = 11000001;

    // pick 0 on the select list is the quest, which has no script row here (the test session's script table is
    // empty): the talk ends with one close (s2c mode 0). Before, a Respond (mode 1) left the client's pending flag
    // set and the talk's buttons dead.
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    [TestCase(0)]
    public void APickWithNoPage_EndsTheTalkWithOneClose(int pick) {
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
            Assert.That(session.NpcScript, Is.Null);
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk).Select(packet => packet[2]),
                Is.EqualTo(new byte[] { 0 })); // one NpcTalk, mode 0 (Close)
        });
    }

    // pick 0 on a select list with one quest whose script has its start state: the talk becomes that quest's talk and
    // its start state is sent (not a close); the talk the handler went on with is the quest's, not the list's.
    [Test]
    public void PickingTheQuest_OpensTheQuestsStartState() {
        const int questId = 10000002;
        var test = new TestGameSession();
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        var state = new ScriptState(100, ScriptStateType.Quest, false, null, [new CinematicContent("", NpcTalkButton.Close, 1, [], [])]);
        test.Register(new ScriptMetadata(questId, ScriptType.Quest, new Dictionary<int, ScriptState> { [100] = state }));
        GameSession session = test.Start();
        test.TakeSent();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(NpcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));

        var list = new NpcScriptManager(session, npc, null, null, NpcTalkType.Select | NpcTalkType.Quest);
        QuestMetadata quest = TestGameSession.QuestData(questId);
        list.Quests = new SortedDictionary<int, QuestMetadata> { [quest.Id] = quest };
        session.NpcScript = list;

        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(0);
        })));
        Assert.Multiple(() => {
            Assert.That(session.NpcScript, Is.Not.Null.And.Not.SameAs(list));
            Assert.That(session.NpcScript!.TalkType, Is.EqualTo(NpcTalkType.Quest));
            Assert.That(session.NpcScript.State?.Id, Is.EqualTo(100));
            Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk).Select(packet => packet[2]).ToList(),
                Has.Count.EqualTo(1).And.None.EqualTo((byte) 0));
        });
    }
}

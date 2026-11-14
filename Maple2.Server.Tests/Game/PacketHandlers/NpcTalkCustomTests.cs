using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// NpcTalk 11 is the pick on a page of a server-sent talk script (CCustomSelectableDistractorCinematicComponent::vfunc_1:
// u8 11, i32 talk npc id, i32 choice index). This server sends no such script, so the pick is read and changes nothing:
// no packet goes out (the client waits for none) and a running talk is kept.
public class NpcTalkCustomTests {
    private const int NpcId = 11000001;

    [Test]
    public void ThePick_IsReadAsNpcIdAndIndex() {
        ByteReader packet = TestGameSession.Packet(writer => {
            writer.WriteInt(NpcId);
            writer.WriteInt(2);
        });

        Assert.Multiple(() => {
            Assert.That(NpcTalkHandler.ReadCustom(packet), Is.EqualTo((NpcId, 2)));
            Assert.That(packet.Available, Is.EqualTo(0));
        });
    }

    [Test]
    public void ThePick_SendsNothingAndKeepsTheTalk() {
        var test = new TestGameSession();
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        GameSession session = test.Start();
        test.TakeSent();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(NpcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        var script = new NpcScriptManager(session, npc, null, null, NpcTalkType.Dialog);
        session.NpcScript = script;

        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        ByteReader packet = TestGameSession.Packet(writer => {
            writer.WriteByte(11);
            writer.WriteInt(NpcId);
            writer.WriteInt(1);
        });
        handler.Handle(session, packet);

        Assert.Multiple(() => {
            Assert.That(packet.Available, Is.EqualTo(0));
            Assert.That(session.NpcScript, Is.SameAs(script));
            Assert.That(test.TakeSent().Count(), Is.EqualTo(0));
        });
    }
}

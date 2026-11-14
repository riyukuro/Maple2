using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// NpcTalk 1 talks only to an npc within the client's talk reach (MyPC::FindTalkableNpc, mypc.cpp:2827): the
// TalkableDistance constant (400 when it is not loaded) plus the npc's capsule radius (worldservice_1.cpp:6438), plus
// the server's one block of slack. A talk from further away opened the npc's talk, shop or quest from anywhere in the
// field; it now gets no answer. The npc id is made up.
public class NpcTalkReachTests {
    private const int Npc = 11900001, ObjectId = 70;
    private const float Radius = 50f;

    private static (TestGameSession Test, GameSession Session) Start(float distance) {
        var test = new TestGameSession();
        test.Register(new ScriptMetadata(Npc, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [10] = new(10, ScriptStateType.Script, true, null, [new CinematicContent("", NpcTalkButton.None, 0, [], [])]),
        }));
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));
        test.SetTable(new QuestMaskNpcTable(new Dictionary<int, IReadOnlyDictionary<int, QuestMaskNpcTable.Mask>>()));
        GameSession session = test.Start();
        session.Player.Position = Vector3.Zero;
        test.TakeSent();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var property = new NpcMetadataProperty([], new NpcMetadataCapsule(Radius, 100), null);
        var metadata = new NpcMetadata(Npc, "npc", "", null!, null!, basic, null!, null!, property, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", any)!.SetValue(npc, new Transform());
        typeof(FieldNpc).GetField(nameof(FieldNpc.SpawnAnimation), any)!.SetValue(npc, "spawn");
        npc.Position = new Vector3(distance, 0, 0);
        session.Field!.Npcs[ObjectId] = npc;
        return (test, session);
    }

    private static List<byte[]> Talk(TestGameSession test, GameSession session) {
        var handler = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(ObjectId);
        }));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NpcTalk).ToList();
    }

    // 400 + 50 radius + 150 slack = 600: a talk at 590 (past 400 + slack, inside only with the radius) starts.
    [Test]
    public void WithinReach_Talks() {
        (TestGameSession test, GameSession session) = Start(590);
        Assert.Multiple(() => {
            Assert.That(Talk(test, session), Is.Not.Empty);
            Assert.That(session.NpcScript?.State?.Id, Is.EqualTo(10));
        });
    }

    [Test]
    public void OutOfReach_GetsNoAnswer() {
        (TestGameSession test, GameSession session) = Start(2000);
        Assert.Multiple(() => {
            Assert.That(Talk(test, session), Is.Empty);
            Assert.That(session.NpcScript, Is.Null);
        });
    }

    [Test]
    public void Reach_IsTheConstantPlusTheRadius() {
        Assert.Multiple(() => {
            Assert.That(Maple2.Model.Validators.InteractRules.TalkReach(0, Radius), Is.EqualTo(450f));
            Assert.That(Maple2.Model.Validators.InteractRules.TalkReach(150, Radius), Is.EqualTo(200f));
        });
    }
}

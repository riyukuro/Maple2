using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// SetDebugMode from a GM, as the client sends it: mode 2 (the object create window's move button, a Vector3S:
// uiobjectcreatedialog.cpp:206) and the NPC
// control window's stop skill button (uinpccontroldialog.cpp:311: mode 4, the npc's object id, command 9).
public class SetDebugModeHandlerTests : NpcCastFixture {
    private static readonly string[] Rig = ["Idle_A", "Attack_Idle_A", "Attack_01_A"];

    private void Gm() => test.Session.Player.AdminPermissions = AdminPermissions.GameMaster;

    private void Send(System.Action<ByteWriter> write) {
        new SetDebugModeHandler().Handle(test.Session, TestGameSession.Packet(write));
    }

    private static void Minimap(ByteWriter writer, short x, short y, short z) {
        writer.WriteByte(2);
        writer.WriteShort(x);
        writer.WriteShort(y);
        writer.WriteShort(z);
    }

    [Test]
    public void Minimap_MovesTheGmToTheEntity() {
        StartCaster(Rig);
        Gm();

        Send(writer => Minimap(writer, 100, 200, 0));

        byte[] packet = test.TakeSent().Single(sent => TestGameSession.Op(sent) == SendOp.UserMoveByPortal);
        var reader = new ByteReader(packet, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(test.Session.Player.ObjectId));
            Assert.That(Vector3.Distance(reader.Read<Vector3>(), new Vector3(100, 200, 25)), Is.LessThan(0.01f));
            Assert.That(reader.Read<Vector3>(), Is.EqualTo(test.Session.Player.Rotation));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.Available, Is.Zero);
        });
    }

    // The map's origin is a point like any other: the client sends it as it is, and it is on this field's navmesh.
    [Test]
    public void Minimap_TheOriginIsAPosition() {
        StartCaster(Rig);
        Gm();

        Send(writer => Minimap(writer, 0, 0, 0));

        Assert.That(test.TakeSent().Count(sent => TestGameSession.Op(sent) == SendOp.UserMoveByPortal), Is.EqualTo(1));
    }

    [Test]
    public void Minimap_NoMoveOffTheField() {
        StartCaster(Rig);
        Gm();

        Send(writer => Minimap(writer, 30000, 30000, 0));

        Assert.That(test.TakeSent().Where(sent => TestGameSession.Op(sent) == SendOp.UserMoveByPortal), Is.Empty);
    }

    [Test]
    public void Minimap_FromAPlainAccount_NoMove() {
        StartCaster(Rig);
        test.Session.Player.AdminPermissions = AdminPermissions.None;

        Send(writer => Minimap(writer, 100, 200, 0));

        Assert.That(test.TakeSent().Where(sent => TestGameSession.Op(sent) == SendOp.UserMoveByPortal), Is.Empty);
    }

    private void StopSkill(int objectId) => Send(writer => {
        writer.WriteByte(4);
        writer.WriteInt(objectId);
        writer.WriteByte(9);
    });

    [Test]
    public void StopSkill_CancelsTheNpcsCast() {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));
        Gm();
        TaskState.NpcTask task = Cast(50100001);
        Assert.That(npc.MovementState.CastTask, Is.SameAs(task));

        StopSkill(npc.ObjectId);

        Assert.Multiple(() => {
            Assert.That(task.IsDone, Is.True);
            Assert.That(npc.MovementState.CastTask, Is.Null);
        });
    }

    [Test]
    public void StopSkill_OnAnIdleNpc_ChangesNothing() {
        StartCaster(Rig);
        Gm();

        StopSkill(npc.ObjectId);

        Assert.Multiple(() => {
            Assert.That(npc.MovementState.CastTask, Is.Null);
            Assert.That(npc.IsDead, Is.False);
        });
    }

    [Test]
    public void StopSkill_FromAPlainAccount_KeepsTheCast() {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));
        TaskState.NpcTask task = Cast(50100001);
        test.Session.Player.AdminPermissions = AdminPermissions.None;

        StopSkill(npc.ObjectId);

        Assert.Multiple(() => {
            Assert.That(task.IsDone, Is.False);
            Assert.That(npc.MovementState.CastTask, Is.SameAs(task));
        });
    }
}

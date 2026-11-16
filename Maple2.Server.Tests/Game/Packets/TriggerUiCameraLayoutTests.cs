using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The Trigger packets the most used trigger actions send, against CTriggerService::FUN_141861840 (client/ms2/
// triggerservice.cpp:366) and the readers it calls: 5 camera path (OnCameraObjects :439): u8 count x int, u8 end; 6 look
// center camera (OnTargetLookCenterCamera :451): u8 sub; 0: vector3, int, int; 1: bool; 2: bool, float; 9 ui (FUN_1418627c0
// :632): u8 sub; 1: int; 2 (show summary): int, int, int; 3: int; 4 (movie): ANSI string, int; 5: int; 7 (emotions): int count
// x unicode string; 0xf (dialog) kind 1: int, int, u8, int, unicode (the timer); 0x14 (FUN_141862d30): u8 state, ANSI string;
// 0x15 (OnNpcDuelHpBar :726): bool, then int x3 when set; 0x17 and 0x18 (WorldService::FUN_141bdad00, worldservice_1.cpp:
// 8894): int count x int, ANSI string; 0x19: none.
public class TriggerUiCameraLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Trigger));
        return reader;
    }

    [Test]
    public void CameraPathAndLookCenter() {
        ByteReader reader = Read(TriggerPacket.CameraStart(new[] { 3, 4 }, true));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 3, 4 }));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.LookCenterCamera(new Vector3(1, 2, 3), 500, 200));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(new[] { reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat() }, Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 500, 200 }));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.LookCenterCameraInnerCheck(true));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.LookCenterCameraYaw(true, 1.5f));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadFloat(), Is.EqualTo(1.5f));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void UiSubModes() {
        ByteReader reader = Read(TriggerPacket.UiGuide(77));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 9, 1 }));
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.UiShowSummary(5, 6, 7));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 9, 2 }));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 5, 6, 7 }));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.UiHideSummary(5));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 9, 3 }));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.UiStartMovie("Movie/Intro.usm", 12));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 9, 4 }));
        Assert.That(reader.ReadString(), Is.EqualTo("Movie/Intro.usm"));
        Assert.That(reader.ReadInt(), Is.EqualTo(12));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.UiSkipMovie(12));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 9, 5 }));
        Assert.That(reader.ReadInt(), Is.EqualTo(12));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.UiEmotionSequence(new[] { "Idle_A", "Bore_A" }));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 9, 7 }));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(new[] { reader.ReadUnicodeString(), reader.ReadUnicodeString() }, Is.EqualTo(new[] { "Idle_A", "Bore_A" }));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void TimerVisibleUiDuelBarAndEffects() {
        var timer = new TickTimer(5000, autoRemove: false, vOffset: 40, type: "Normal");
        ByteReader reader = Read(TriggerPacket.TimerDialog(timer));
        Assert.That(new[] { reader.ReadByte(), reader.ReadByte() }, Is.EqualTo(new byte[] { 15, 1 }));
        Assert.That(reader.ReadInt(), Is.EqualTo(timer.StartTick));
        Assert.That(reader.ReadInt(), Is.EqualTo(5000));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(40));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Normal"));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.SetVisibleUi(true, "MiniMap,QuestList"));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x14));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadString(), Is.EqualTo("MiniMap,QuestList"));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.DuelHpBar(true, 31, 6000, 4));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x15));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 31, 6000, 4 }));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.DuelHpBar(false));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x15));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TriggerPacket.Effect2(new[] { 8, 9 }, "Eff_Sparkle"));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x18));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 8, 9 }));
        Assert.That(reader.ReadString(), Is.EqualTo("Eff_Sparkle"));
        Assert.That(reader.Available, Is.Zero);
    }

    // Update (3) of the objects the triggers switch most (sound 0x2147ae01, camera 0x65338ec3, cube 0x79197a62, effect
    // 0x670a04d7, rope 0x8a9638bf; TriggerObject::Create, triggeractioncli.cpp:450, and their vfunc_2 readers :555-848): int
    // id, then the state the class reads: sound, camera and cube the visible bool only; effect bool, bool, int; rope bool,
    // bool, int. FUN_141863a90 (triggerservice.cpp:951) reads the id first.
    [Test]
    public void ObjectUpdates() {
        foreach (Maple2.Model.Game.ITriggerObject simple in new Maple2.Model.Game.ITriggerObject[] {
                     new Maple2.Model.Game.TriggerObjectSound(new Maple2.Model.Metadata.Ms2TriggerSound(21, false)) { Visible = true },
                     new Maple2.Model.Game.TriggerObjectCamera(new Maple2.Model.Metadata.Ms2TriggerCamera(22, false)) { Visible = true },
                     new Maple2.Model.Game.TriggerObjectCube(new Maple2.Model.Metadata.Ms2TriggerCube(23, false)) { Visible = true },
                 }) {
            ByteReader reader = Read(TriggerPacket.Update(simple));
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(reader.ReadInt(), Is.EqualTo(simple.Id));
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.Available, Is.Zero);
        }

        var effect = new Maple2.Model.Game.TriggerObjectEffect(new Maple2.Model.Metadata.Ms2TriggerEffect(24, false)) { Visible = true, UnknownBool = true, UnknownInt = 9 };
        ByteReader update = Read(TriggerPacket.Update(effect));
        Assert.That(update.ReadByte(), Is.EqualTo(3));
        Assert.That(update.ReadInt(), Is.EqualTo(24));
        Assert.That(new[] { update.ReadBool(), update.ReadBool() }, Is.EqualTo(new[] { true, true }));
        Assert.That(update.ReadInt(), Is.EqualTo(9));
        Assert.That(update.Available, Is.Zero);

        var rope = new Maple2.Model.Game.TriggerObjectRope(new Maple2.Model.Metadata.Ms2TriggerRope(25, false)) { Visible = true, Animate = true, Delay = 3 };
        update = Read(TriggerPacket.Update(rope));
        Assert.That(update.ReadByte(), Is.EqualTo(3));
        Assert.That(update.ReadInt(), Is.EqualTo(25));
        Assert.That(new[] { update.ReadBool(), update.ReadBool() }, Is.EqualTo(new[] { true, true }));
        Assert.That(update.ReadInt(), Is.EqualTo(3));
        Assert.That(update.Available, Is.Zero);
    }
}

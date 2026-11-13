using System;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// Trigger against CSceneStateGame::OnTrigger and CTriggerService (triggerservice.cpp). Load and Update reach
// FUN_141863a90 (:951): int id, then the block of the client's TriggerObject class for the entity's model
// (TriggerObject::Create, triggeractioncli.cpp:450).
public class TriggerLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // Mesh: int, bool visible, bool minimap, int fade, unicode, float scale. Ladder (the client's rope class, model
    // 0xc680f858): int, bool, bool animate, int delay. Agent (the base class): int only. Actor: int, bool, unicode.
    [Test]
    public void LoadAndUpdate() {
        var mesh = new TriggerObjectMesh(new Ms2TriggerMesh(1.5f, 0x11, true, true)) { Fade = 0x21 };
        var ladder = new TriggerObjectLadder(new Ms2TriggerLadder(0x12, true)) { Visible = true, Animate = true, Delay = 0x22 };
        var agent = new TriggerObjectAgent(new Ms2TriggerAgent(0x13, true));
        var expected = new ByteWriter();
        expected.WriteByte(2);
        expected.WriteInt(3);
        expected.WriteInt(0x11);
        expected.WriteBool(true);
        expected.WriteBool(true);
        expected.WriteInt(0x21);
        expected.WriteUnicodeString("");
        expected.WriteFloat(1.5f);
        expected.WriteInt(0x12);
        expected.WriteBool(true);
        expected.WriteBool(true);
        expected.WriteInt(0x22);
        expected.WriteInt(0x13);
        Assert.That(Body(TriggerPacket.Load(new ITriggerObject[] { mesh, ladder, agent })), Is.EqualTo(expected.ToArray()));

        var actor = new TriggerObjectActor(new Ms2TriggerActor("Idle_A", 0x14, true)) { Visible = true, SequenceName = "Idle_A" };
        var update = new ByteWriter();
        update.WriteByte(3);
        update.WriteInt(0x14);
        update.WriteBool(true);
        update.WriteUnicodeString("Idle_A");
        Assert.That(Body(TriggerPacket.Update(actor)), Is.EqualTo(update.ToArray()));
    }

    // 16: int n, n x (int key, int object id, narrow nif, bool, float, float); 17: the block alone; 18: int.
    [Test]
    public void Effects() {
        var load = new ByteWriter();
        load.WriteByte(16);
        load.WriteInt(1);
        load.WriteInt(0x31);
        load.WriteInt(0x31);
        load.WriteString("nif");
        load.WriteBool(true);
        load.WriteFloat(2);
        load.WriteFloat(90);
        Assert.That(Body(TriggerPacket.LoadEffects(new[] { (0x31, "nif", true, 2f, 90f) })), Is.EqualTo(load.ToArray()));

        var add = new ByteWriter();
        add.WriteByte(17);
        add.WriteInt(0x31);
        add.WriteString("nif");
        add.WriteBool(true);
        add.WriteFloat(2);
        add.WriteFloat(90);
        Assert.That(Body(TriggerPacket.AddEffect(0x31, "nif", true, 2, 90)), Is.EqualTo(add.ToArray()));

        var remove = new ByteWriter();
        remove.WriteByte(18);
        remove.WriteInt(0x31);
        Assert.That(Body(TriggerPacket.RemoveEffect(0x31)), Is.EqualTo(remove.ToArray()));

        // 23 and 24 (FUN_141bdad00, worldservice_1.cpp:8894): int n, n x int, narrow string; 25: nothing.
        var effect = new ByteWriter();
        effect.WriteByte(23);
        effect.WriteInt(2);
        effect.WriteInt(0x41);
        effect.WriteInt(0x42);
        effect.WriteString("fx");
        Assert.That(Body(TriggerPacket.Effect1(new[] { 0x41, 0x42 }, "fx")), Is.EqualTo(effect.ToArray()));
        Assert.That(Body(TriggerPacket.EffectStop()), Is.EqualTo(new byte[] { 25 }));
    }

    // 19: byte type, int duration, narrow usm, illustration, voice, sound, unicode script (the order the client's
    // xml loader gives these fields, uisidepopuptalk_3.cpp:247).
    [Test]
    public void SidePopup() {
        var expected = new ByteWriter();
        expected.WriteByte(19);
        expected.WriteByte((byte) SideNpcTalkType.CutIn);
        expected.WriteInt(0x51);
        expected.WriteString("usm");
        expected.WriteString("illust");
        expected.WriteString("voice");
        expected.WriteString("sound");
        expected.WriteUnicodeString("script");
        Assert.That(Body(TriggerPacket.SidePopupTalk(SideNpcTalkType.CutIn, 0x51, "illust", "voice", "script", "sound", "usm")),
            Is.EqualTo(expected.ToArray()));
    }

    // 9 / 8: bool loop, int duration, unicode; 9 / 9: int, narrow; 9 / 10 and 9 / 11: Vector3Short, bool.
    [Test]
    public void Ui() {
        var loop = new ByteWriter();
        loop.WriteByte(9);
        loop.WriteByte(8);
        loop.WriteBool(true);
        loop.WriteInt(0x61);
        loop.WriteUnicodeString("seq");
        Assert.That(Body(TriggerPacket.UiEmotionLoop("seq", 0x61, true)), Is.EqualTo(loop.ToArray()));

        var face = new ByteWriter();
        face.WriteByte(9);
        face.WriteByte(9);
        face.WriteInt(0x62);
        face.WriteString("smile");
        Assert.That(Body(TriggerPacket.UiFaceEmotion(0x62, "smile")), Is.EqualTo(face.ToArray()));

        var rotation = new ByteWriter();
        rotation.WriteByte(9);
        rotation.WriteByte(10);
        rotation.WriteShort(1);
        rotation.WriteShort(2);
        rotation.WriteShort(3);
        rotation.WriteBool(true);
        Assert.That(Body(TriggerPacket.UiPlayerRotation(new Vector3S(1, 2, 3), true)), Is.EqualTo(rotation.ToArray()));

        var look = new ByteWriter();
        look.WriteByte(9);
        look.WriteByte(11);
        look.WriteShort(4);
        look.WriteShort(5);
        look.WriteShort(6);
        look.WriteBool(false);
        Assert.That(Body(TriggerPacket.UiLookAt(new Vector3(4, 5, 6))), Is.EqualTo(look.ToArray()));
    }

    // 15 / 2 (pvpinfo.cpp:193): int tick, int duration, bool, 2 x (long, int, int, byte). 15 / 5 (:222): the same
    // with an int rating per fighter, then an int camera step. The tick is the server's clock and is skipped.
    [Test]
    public void PvpDialogs() {
        var first = new TriggerPacket.PvpFighter(0x0102030405060708, 0x71, 0x72, 1, 0x73);
        var second = new TriggerPacket.PvpFighter(0x1112131415161718, 0x74, 0x75, 2, 0x76);

        var observer = new ByteWriter();
        observer.WriteInt(0x81);
        observer.WriteBool(true);
        foreach (var fighter in new[] { first, second }) {
            observer.WriteLong(fighter.CharacterId);
            observer.WriteInt(fighter.Hp);
            observer.WriteInt(fighter.MaxHp);
            observer.WriteByte(fighter.Wins);
        }
        byte[] body = Body(TriggerPacket.PvpObserverDialog(0x81, first, second));
        Assert.That(body[..2], Is.EqualTo(new byte[] { 15, 2 }));
        Assert.That(body[6..], Is.EqualTo(observer.ToArray()));

        var player = new ByteWriter();
        player.WriteInt(0x81);
        player.WriteBool(false);
        foreach (var fighter in new[] { first, second }) {
            player.WriteLong(fighter.CharacterId);
            player.WriteInt(fighter.Hp);
            player.WriteInt(fighter.MaxHp);
            player.WriteByte(fighter.Wins);
            player.WriteInt(fighter.Rating);
        }
        player.WriteInt(0x82);
        body = Body(TriggerPacket.PvpPlayerDialog(0x81, first, second, 0x82, false));
        Assert.That(body[..2], Is.EqualTo(new byte[] { 15, 5 }));
        Assert.That(body[6..], Is.EqualTo(player.ToArray()));
    }
}

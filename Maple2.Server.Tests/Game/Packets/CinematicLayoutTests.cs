using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// Cinematic against UICutSceneCinematic::OnPacket (uicutscenecinematic.cpp, 0x141908290) and OneTimeEffect against
// CSceneStateGame::OnOneTimeEffect (scenestategame.cpp:8453).
public class CinematicLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // 1: bool; 2, 5, 7: nothing; 3: int transition, unicode script, unicode; 4: byte kind, narrow string.
    [Test]
    public void UiViewAndSkip() {
        Assert.That(Body(CinematicPacket.ToggleUi(true)), Is.EqualTo(new byte[] { 1, 1 }));
        Assert.That(Body(CinematicPacket.HideUi()), Is.EqualTo(new byte[] { 2 }));
        Assert.That(Body(CinematicPacket.StartSkip()), Is.EqualTo(new byte[] { 5 }));
        Assert.That(Body(CinematicPacket.RemoveTalk()), Is.EqualTo(new byte[] { 7 }));

        var views = new (Func<string, ByteWriter> Write, int Transition)[] {
            (CinematicPacket.LetterboxTransition, 3), (CinematicPacket.FadeTransition, 4),
            (CinematicPacket.HorizontalTransition, 5), (CinematicPacket.VerticalTransition, 6),
        };
        foreach ((Func<string, ByteWriter> write, int transition) in views) {
            var view = new ByteWriter();
            view.WriteByte(3);
            view.WriteInt(transition);
            view.WriteUnicodeString("script");
            view.WriteUnicodeString("");
            Assert.That(Body(write("script")), Is.EqualTo(view.ToArray()));
        }

        var scene = new ByteWriter();
        scene.WriteByte(4);
        scene.WriteByte(1);
        scene.WriteString("scene");
        Assert.That(Body(CinematicPacket.SetSkipScene("scene")), Is.EqualTo(scene.ToArray()));
        var state = new ByteWriter();
        state.WriteByte(4);
        state.WriteByte(2);
        state.WriteString("state");
        Assert.That(Body(CinematicPacket.SetSkipState("state")), Is.EqualTo(state.ToArray()));
    }

    // 6: int npc, narrow illustration, unicode script, int duration, byte align. 8: byte table, int object, unicode,
    // int duration, int delay. 9: int object.
    [Test]
    public void Talks() {
        var talk = new ByteWriter();
        talk.WriteByte(6);
        talk.WriteInt(0x11);
        talk.WriteString("illust");
        talk.WriteUnicodeString("script");
        talk.WriteInt(0x12);
        talk.WriteByte((byte) Align.right);
        Assert.That(Body(CinematicPacket.Talk(0x11, "illust", "script", 0x12, Align.right)), Is.EqualTo(talk.ToArray()));

        var balloon = new ByteWriter();
        balloon.WriteByte(8);
        balloon.WriteByte(1);
        balloon.WriteInt(0x21);
        balloon.WriteUnicodeString("script");
        balloon.WriteInt(0x22);
        balloon.WriteInt(0x23);
        Assert.That(Body(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.NpcAi, 0x21, "script", 0x22, 0x23)),
            Is.EqualTo(balloon.ToArray()));

        var remove = new ByteWriter();
        remove.WriteByte(9);
        remove.WriteInt(0x21);
        Assert.That(Body(CinematicPacket.RemoveBalloonTalk(0x21)), Is.EqualTo(remove.ToArray()));
    }

    // 10: 4 x unicode (type, title, script, align name), int duration, 3 x float. 11: unicode, bool. 12: unicode.
    [Test]
    public void CaptionOpeningIntro() {
        var caption = new ByteWriter();
        caption.WriteByte(10);
        caption.WriteUnicodeString("type");
        caption.WriteUnicodeString("title");
        caption.WriteUnicodeString("script");
        caption.WriteUnicodeString("bottomLeft");
        caption.WriteInt(0x31);
        caption.WriteFloat(0.25f);
        caption.WriteFloat(0.5f);
        caption.WriteFloat(2);
        Assert.That(Body(CinematicPacket.Caption("type", "title", "script", Align.bottomLeft, 0.25f, 0.5f, 0x31, 2)),
            Is.EqualTo(caption.ToArray()));

        var opening = new ByteWriter();
        opening.WriteByte(11);
        opening.WriteUnicodeString("script");
        opening.WriteBool(true);
        Assert.That(Body(CinematicPacket.Opening("script", true)), Is.EqualTo(opening.ToArray()));

        var intro = new ByteWriter();
        intro.WriteByte(12);
        intro.WriteUnicodeString("script");
        Assert.That(Body(CinematicPacket.Intro("script")), Is.EqualTo(intro.ToArray()));
    }

    // OneTimeEffect: int id, bool enable, then the unicode path only when enabled.
    [Test]
    public void OneTimeEffect() {
        var on = new ByteWriter();
        on.WriteInt(0x41);
        on.WriteBool(true);
        on.WriteUnicodeString("path");
        Assert.That(Body(CinematicPacket.OneTimeEffect(0x41, true, "path")), Is.EqualTo(on.ToArray()));

        var off = new ByteWriter();
        off.WriteInt(0x41);
        off.WriteBool(false);
        Assert.That(Body(CinematicPacket.OneTimeEffect(0x41, false, "path")), Is.EqualTo(off.ToArray()));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// SetCraftModeHandler in the order and with the bytes KMS2 sends for RequestSetCraftMode.
public class SetCraftModeHandlerTests {
    private readonly SetCraftModeHandler handler = new();

    private const string CUBE = "EBFEFD02" + "622C0C0E779C2F24" + "0000000000000000" + "00";

    private void Send(GameSession session, string hex) {
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteBytes(Convert.FromHexString(hex))));
    }

    private static string Hex(byte[] packet) => Convert.ToHexString(packet);

    // A session in its own home: the plot it may build on.
    private static GameSession InOwnHome(TestGameSession test) {
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        test.TakeSent();
        return session;
    }

    // Start: the guide (with the player's heading), then the mode with the cube the client sent. Stop: the
    // mode, then the guide's removal.
    [Test]
    public void StartAndStop_SendTheGuideAndTheModeInOrder() {
        var test = new TestGameSession();
        GameSession session = InOwnHome(test);
        session.Player.Rotation = new Vector3(0, 0, 270);
        test.TakeSent();

        Send(session, "01" + "01" + CUBE);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.GuideObject, SendOp.SetCraftMode }));
        byte[] guide = sent[0];
        Assert.That(guide.Length, Is.EqualTo(2 + 47));
        Assert.That(Hex(guide[2..5]), Is.EqualTo("000000"));
        Assert.That(BitConverter.ToInt32(guide, 5), Is.EqualTo(session.GuideObject!.ObjectId));
        Assert.That(BitConverter.ToInt64(guide, 9), Is.EqualTo(session.CharacterId));
        Assert.That(Hex(guide[29..]), Is.EqualTo("00000000" + "00000000" + "00008743" + "0100000000000000"));
        string id = Hex(BitConverter.GetBytes(session.Player.ObjectId));
        Assert.That(Hex(sent[1]), Is.EqualTo("6800" + id + "01" + CUBE + "00000000"));

        int guideId = session.GuideObject.ObjectId;
        Send(session, "00" + "00");

        sent = test.TakeSent();
        Assert.That(sent.Select(Hex), Is.EqualTo(new[] {
            "6800" + id + "00",
            "6E00" + "01" + Hex(BitConverter.GetBytes(guideId)) + Hex(BitConverter.GetBytes(session.CharacterId)),
        }));
        Assert.That(session.GuideObject, Is.Null);
        Assert.That(session.HeldCube, Is.Null);
    }

    // A start without a cube (second byte 0) and a stop without a guide send nothing.
    [Test]
    public void StartWithoutACubeAndStopWithoutAGuide_SendNothing() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        Send(session, "01" + "00");
        Send(session, "00" + "00");

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.GuideObject, Is.Null);
    }

    // The client asks for the build mode only where it may build (MyPC::FUN_14152feb0): a start outside a plot of
    // its own (or of a home that lets it build), or while dead, holds nothing and sends nothing.
    [Test]
    public void StartWhereItMayNotBuild_SendsNothing() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();

        Send(session, "01" + "01" + CUBE);
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.GuideObject, Is.Null);
        Assert.That(session.HeldCube, Is.Null);
    }

    [Test]
    public void StartWhileDead_SendsNothing() {
        var test = new TestGameSession();
        GameSession session = InOwnHome(test);
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session.Player, true);

        Send(session, "01" + "01" + CUBE);
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.GuideObject, Is.Null);
    }
}

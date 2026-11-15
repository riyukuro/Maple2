using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube's home options dialog: permission rows (42, 43), background (51), lighting (52) and camera (54). The
// owner changes them in its own home; a saved change is announced to the field, an unchanged or undefined value is not.
public class RequestCubeHomeOptionsTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private RequestCubeHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterHome();
        handler = new RequestCubeHandler { TableMetadata = test.TableMetadata };
        test.TakeSent();
    }

    private void Send(byte command, Action<ByteWriter>? body = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void EnablePermission_Saved_Announced() {
        Send(42, writer => { writer.WriteByte((byte) HomePermission.Jump); writer.WriteBool(true); });

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Permissions.ContainsKey(HomePermission.Jump), Is.True);
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.EnablePermission(HomePermission.Jump, true))));
        });
    }

    [Test]
    public void EnablePermission_Disabled_RowRemoved() {
        Send(42, writer => { writer.WriteByte((byte) HomePermission.Climb); writer.WriteBool(true); });
        test.TakeSent();

        Send(42, writer => { writer.WriteByte((byte) HomePermission.Climb); writer.WriteBool(false); });

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Permissions.ContainsKey(HomePermission.Climb), Is.False);
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.EnablePermission(HomePermission.Climb, false))));
        });
    }

    [Test]
    public void EnablePermission_RowTheClientLacks_Ignored() {
        Send(42, writer => { writer.WriteByte(9); writer.WriteBool(true); });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void SetPermission_OfAnEnabledRow_Saved() {
        Send(42, writer => { writer.WriteByte((byte) HomePermission.Skill); writer.WriteBool(true); });
        test.TakeSent();

        Send(43, writer => { writer.WriteByte((byte) HomePermission.Skill); writer.WriteByte((byte) HomePermissionSetting.Party); });

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Permissions[HomePermission.Skill], Is.EqualTo(HomePermissionSetting.Party));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.SetPermission(HomePermission.Skill, HomePermissionSetting.Party))));
        });
    }

    [Test]
    public void SetPermission_OfADisabledRow_StaysAtZero() {
        Send(43, writer => { writer.WriteByte((byte) HomePermission.Music); writer.WriteByte((byte) HomePermissionSetting.Self); });

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Permissions.ContainsKey(HomePermission.Music), Is.False);
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.SetPermission(HomePermission.Music, HomePermissionSetting.None))));
        });
    }

    [TestCase((byte) 9, (byte) 1)]
    [TestCase((byte) 0, (byte) 3)]
    public void SetPermission_OutOfRange_Ignored(byte permission, byte setting) {
        Send(43, writer => { writer.WriteByte(permission); writer.WriteByte(setting); });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void SetBackground_Saved_Announced() {
        Send(51, writer => writer.WriteByte((byte) HomeBackground.BlueSea));

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Background, Is.EqualTo(HomeBackground.BlueSea));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.SetBackground(HomeBackground.BlueSea))));
        });
    }

    [Test]
    public void SetBackground_SameOrUndefined_Silent() {
        Send(51, writer => writer.WriteByte((byte) session.Player.Value.Home.Background));
        Send(51, writer => writer.WriteByte(200));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void SetLighting_Saved_Announced() {
        Send(52, writer => writer.WriteByte((byte) HomeLighting.Dusk));

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Lighting, Is.EqualTo(HomeLighting.Dusk));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.SetLighting(HomeLighting.Dusk))));
        });
    }

    [Test]
    public void SetLighting_SameOrUndefined_Silent() {
        Send(52, writer => writer.WriteByte((byte) session.Player.Value.Home.Lighting));
        Send(52, writer => writer.WriteByte(200));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void SetCamera_Saved_Announced() {
        Send(54, writer => writer.WriteByte((byte) HomeCamera.TopView));

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Camera, Is.EqualTo(HomeCamera.TopView));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.SetCamera(HomeCamera.TopView))));
        });
    }

    [Test]
    public void SetCamera_SameOrUndefined_Silent() {
        Send(54, writer => writer.WriteByte((byte) session.Player.Value.Home.Camera));
        Send(54, writer => writer.WriteByte(200));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void KickOut_NoVisitors_Silent() {
        Send(49);

        Assert.That(test.TakeSent(), Is.Empty);
    }
}

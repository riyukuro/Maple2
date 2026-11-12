using System.Linq;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The home option dialog shows the open type of the owner's house info (ResponseCube 0x14, the last byte:
// UserUGCMapInfo +0x40, FUN_1408b4220), and the reply to a setting (0x18) changes nothing on the client. So 0x14
// carries the stored setting, and the owner gets it again after a change.
public class HomeOpenTypeTests {
    [TestCase("", HomeOpenRules.Public)]
    [TestCase(null, HomeOpenRules.Public)]
    [TestCase("123456", HomeOpenRules.Password)]
    [TestCase(HomeOpenRules.PrivatePasscode, HomeOpenRules.Private)]
    public void OpenType_IsTheKindOfTheStoredPasscode(string? passcode, byte expected) {
        Assert.That(HomeOpenRules.OpenType(passcode), Is.EqualTo(expected));
    }

    [TestCase("", HomeOpenRules.Public)]
    [TestCase("123456", HomeOpenRules.Password)]
    [TestCase(HomeOpenRules.PrivatePasscode, HomeOpenRules.Private)]
    public void UpdateProfile_EndsWithTheOpenType(string passcode, byte expected) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.Value.Home.Passcode = passcode;

        byte[] packet = CubePacket.UpdateProfile(session.Player).ToArray();

        Assert.That(packet[2], Is.EqualTo(0x14));
        Assert.That(packet[^1], Is.EqualTo(expected));
    }

    [TestCase(HomeOpenRules.Public, "", "")]
    [TestCase(HomeOpenRules.Password, "123456", "123456")]
    [TestCase(HomeOpenRules.Private, "", HomeOpenRules.PrivatePasscode)]
    public void Setting_TheOwnerGetsTheNewOpenType(byte kind, string text, string stored) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterHome();
        session.Player.Value.Home.Passcode = kind == HomeOpenRules.Public ? "654321" : string.Empty;
        test.TakeSent();

        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(24);
            writer.WriteByte(kind);
            writer.WriteUnicodeString(text);
            writer.WriteByte(1);
        }));

        Assert.That(session.Player.Value.Home.Passcode, Is.EqualTo(stored));
        byte[][] cube = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ResponseCube).ToArray();
        Assert.That(cube.Select(packet => packet[2]), Is.EqualTo(new byte[] { 0x18, 0x14 }));
        Assert.That(cube[1][^1], Is.EqualTo(kind));
    }
}

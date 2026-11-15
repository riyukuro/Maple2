using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube's home settings: name (21), passcode (24), message (29), the build mode answer (0) and the sub-types
// the server only reads. The sender edits its own home; the client's limits (Constant.HomeNameMaxLength, the message
// length, the passcode kinds of MyPC::SendHouseSetting) hold, and a refusal is ResponseCube's centre error.
public class RequestCubeSettingsTests {
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
    private static readonly string CenterError = Hex(CubePacket.Error(UgcMapError.s_ugcmap_center));

    [Test]
    public void SetHomeName_Saved_AnnouncedToTheField() {
        Send(21, writer => writer.WriteUnicodeString("My Home"));

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo("My Home"));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.SetHomeName(session.Player.Value.Home))));
        });
    }

    [Test]
    public void SetHomeName_TooLong_Refused() {
        string before = session.Player.Value.Home.Indoor.Name;

        Send(21, writer => writer.WriteUnicodeString(new string('n', Constant.HomeNameMaxLength + 1)));

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo(before));
            Assert.That(Sent(), Is.EqualTo(new[] { CenterError }));
        });
    }

    [Test]
    public void SetHomeMessage_Saved_AnnouncedToTheField() {
        Send(29, writer => writer.WriteUnicodeString("welcome"));

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Message, Is.EqualTo("welcome"));
            Assert.That(Sent(), Does.Contain(Hex(CubePacket.SetHomeMessage("welcome"))));
        });
    }

    [Test]
    public void SetHomeMessage_TooLong_Refused() {
        Send(29, writer => writer.WriteUnicodeString(new string('m', Constant.HomeMessageMaxLength + 1)));

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Message, Is.Not.EqualTo(new string('m', Constant.HomeMessageMaxLength + 1)));
            Assert.That(Sent(), Is.EqualTo(new[] { CenterError }));
        });
    }

    private void Passcode(byte kind, string text) => Send(24, writer => {
        writer.WriteByte(kind);
        writer.WriteUnicodeString(text);
        writer.WriteByte(0);
    });

    [Test]
    public void SetPasscode_Password_SavedAndAnswered() {
        Passcode(1, "123456");

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Passcode, Is.EqualTo("123456"));
            Assert.That(Sent().Take(2), Is.EqualTo(new[] { Hex(CubePacket.SetPasscode()), Hex(CubePacket.UpdateProfile(session.Player)) }));
        });
    }

    [Test]
    public void SetPasscode_NotDigits_SavedLikeTheOptionDialogAllows() {
        Passcode(1, "abc12!");

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Passcode, Is.EqualTo("abc12!"));
            Assert.That(Sent().Take(2), Is.EqualTo(new[] { Hex(CubePacket.SetPasscode()), Hex(CubePacket.UpdateProfile(session.Player)) }));
        });
    }

    // An owner on Passcode who presses Apply in the home option dialog without retyping the password (the
    // "******" case) keeps the passcode and gets the normal answer, not the centre error.
    [Test]
    public void SetPasscode_UnchangedMask_KeepsThePasscode() {
        Passcode(1, "123456");
        test.TakeSent();

        Send(24, writer => {
            writer.WriteByte(1);
            writer.WriteUnicodeString(HomeOpenRules.UnchangedPasscode);
            writer.WriteByte(1);
        });

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Passcode, Is.EqualTo("123456"));
            Assert.That(Sent().Take(2), Is.EqualTo(new[] { Hex(CubePacket.SetPasscode()), Hex(CubePacket.UpdateProfile(session.Player)) }));
        });
    }

    [Test]
    public void SetPasscode_UnchangedMaskOnAPrivateHome_RefusedAndStaysPrivate() {
        Passcode(2, "");
        test.TakeSent();

        Passcode(1, HomeOpenRules.UnchangedPasscode);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Passcode, Is.EqualTo(HomeOpenRules.PrivatePasscode));
            Assert.That(Sent(), Is.EqualTo(new[] { CenterError }));
        });
    }

    [Test]
    public void SetPasscode_Public_ClearsIt() {
        Passcode(1, "123456");
        test.TakeSent();

        Passcode(0, "");

        Assert.That(session.Player.Value.Home.Passcode, Is.Empty);
    }

    [Test]
    public void SetPasscode_Private_UsesTheCommonCode() {
        Passcode(2, "");

        Assert.That(session.Player.Value.Home.Passcode, Is.EqualTo(HomeOpenRules.PrivatePasscode));
    }

    [TestCase((byte) 1, "12345", TestName = "SetPasscode_PasswordTooShort_Refused")]
    [TestCase((byte) 1, HomeOpenRules.UnchangedPasscode, TestName = "SetPasscode_UnchangedWithNoPassword_Refused")]
    [TestCase((byte) 3, "123456", TestName = "SetPasscode_UnknownKind_Refused")]
    public void SetPasscode_Refused(byte kind, string text) {
        Passcode(kind, text);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Passcode, Is.Null);
            Assert.That(Sent(), Is.EqualTo(new[] { CenterError }));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void BuildState_AnsweredToTheField(int mode) {
        Send(0, writer => writer.WriteInt(mode));

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CubePacket.BuildMode(session.Player.ObjectId, mode)) }));
    }

    [Test]
    public void BuildState_OtherMode_Silent() {
        Send(0, writer => writer.WriteInt(2));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // Sub-types the server only reads: nothing is sent back.
    [Test]
    public void ReadOnlySubTypes_Silent() {
        Send(13, writer => writer.WriteInt(1));
        Send(5, writer => { writer.WriteInt(1); writer.WriteInt(0); });
        Send(30, writer => { writer.WriteUnicodeString("cube"); writer.WriteUnicodeString("name"); });
        Send(19, writer => writer.WriteInt(1));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void VoteHome_OutsideAHome_AnswersNoOwner() {
        TestGameSession outside = new();
        outside.Start();
        var outsideHandler = new RequestCubeHandler { TableMetadata = outside.TableMetadata };
        outside.TakeSent();

        outsideHandler.Handle(outside.Session, TestGameSession.Packet(writer => writer.WriteByte(25)));

        Assert.That(outside.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(CubePacket.ConfirmVote((byte) UgcMapError.s_ugcmap_no_owner_to_commend)) }));
    }
}

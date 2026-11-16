using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Global.Service;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using GlobalClient = Maple2.Server.Global.Service.Global.GlobalClient;

namespace Maple2.Server.Tests.Login;

// The login request (ResponseLogin): mode 1 from the server list button, mode 2 from the character list button. The
// account server's answer is what the client shows: a refused login gets LoginResult with the client's code (and for
// a restricted account the ban's reason and times), then the connection ends.
public class LoginResultTests {
    private sealed class FakeGlobal : GlobalClient {
        public LoginResponse Response = new();
        public readonly List<LoginRequest> Requests = [];

        public override LoginResponse Login(LoginRequest request, CallOptions options) {
            Requests.Add(request);
            return Response;
        }
    }

    private TestLoginSession test = null!;
    private FakeGlobal global = null!;
    private LoginHandler handler = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        global = new FakeGlobal { Response = new LoginResponse { Code = LoginResponse.Types.Code.Ok, AccountId = test.AccountId } };
        handler = new LoginHandler { Global = global, World = test.World };
        session = test.Start(init: false);
        TestLoginSession.SetField(session, "name", "127.0.0.1:1");
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void Login(byte command, string user = "user", string pass = "pass") {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteUnicodeString(user);
            writer.WriteUnicodeString(pass);
            writer.WriteShort(0);
            writer.WriteBytes(new byte[16]);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteString("");
        }));
    }

    private string[] SentHex() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    // Whether the session still sends: a closed one keeps nothing.
    private bool Open() {
        session.Send(NoticePacket.Disconnect(new Maple2.Model.Game.InterfaceText("probe")));
        return test.TakeSent().Count == 1;
    }

    [TestCase(LoginResponse.Types.Code.ErrorId)]
    [TestCase(LoginResponse.Types.Code.ErrorPassword)]
    [TestCase(LoginResponse.Types.Code.FullServer)]
    public void RefusedLogin_ClientsCode_ThenClosed(LoginResponse.Types.Code code) {
        global.Response = new LoginResponse { Code = code, Message = "refused", AccountId = 0 };

        Login(2);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] { Convert.ToHexString(LoginResultPacket.Error((byte) code, "refused", 0).ToArray()) }));
            Assert.That(global.Requests.Select(request => (request.Username, request.Password)), Is.EqualTo(new[] { ("user", "pass") }));
            Assert.That(test.World.EndedTokens, Is.Empty);
            Assert.That(Open(), Is.False);
        });
    }

    [Test]
    public void RestrictedAccount_TheBansReasonAndTimes_ThenClosed() {
        global.Response = new LoginResponse {
            Code = LoginResponse.Types.Code.Restricted, Message = "reason", AccountId = test.AccountId, BanStart = 1700000000, BanExpiry = 1800000000,
        };

        Login(2);

        byte[] expected = LoginResultPacket.Restricted("reason", test.AccountId, 1700000000, 1800000000).ToArray();
        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] { Convert.ToHexString(expected) }));
            // The client's code for a restricted login, then the reason and the account.
            Assert.That(expected[2], Is.EqualTo((byte) LoginResponse.Types.Code.Restricted));
            Assert.That(Open(), Is.False);
        });
    }

    // The server list button: the banners and the server list, then the connection ends (the client logs in again for
    // the character list).
    [Test]
    public void ServerList_BannersAndServers_ThenClosed() {
        test.World.Online = 7;

        Login(1);

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.BannerList, SendOp.ServerList }));
            Assert.That(session.HasAccount, Is.False);
            Assert.That(Open(), Is.False);
        });
    }

    // A world that cannot list its channels: nothing is sent and the connection is cut.
    [Test]
    public void ServerList_WorldDown_Cut() {
        test.World.ChannelsError = new RpcException(new Status(StatusCode.Unavailable, "down"));

        Login(1);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(Open(), Is.False);
        });
    }

    [Test]
    public void CharacterList_SuccessEndpointAndList() {
        Login(2);

        List<SendOp> ops = test.TakeSentOps();
        Assert.Multiple(() => {
            Assert.That(ops.Take(2), Is.EqualTo(new[] { SendOp.LoginResult, SendOp.Ugc }));
            Assert.That(ops, Does.Contain(SendOp.CharacterList));
            Assert.That(session.HasAccount, Is.True);
            Assert.That(session.AccountId, Is.EqualTo(test.AccountId));
        });
    }

    // A mode the client does not write: nothing is sent and the connection is cut.
    [TestCase((byte) 0)]
    [TestCase((byte) 3)]
    public void UnknownMode_Cut(byte mode) {
        Login(mode);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.HasAccount, Is.False);
            Assert.That(Open(), Is.False);
        });
    }

    // The account has a character list open on another connection: that one is cut, this one told the account is in
    // use (the client's kick text), and the list stays with nobody until the client logs in again.
    [Test]
    public void AccountOnTheCharacterListElsewhere_ThatOneCut_AlreadyLogin() {
        var other = new TestLoginSession();
        LoginSession existing = other.Start();
        TestLoginSession.SetField(existing, "name", "127.0.0.1:2");
        TestLoginSession.SetField(existing, "<AccountId>k__BackingField", test.AccountId);
        session.Server.OnConnected(existing);

        Login(2);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] {
                Convert.ToHexString(LoginResultPacket.Error((byte) LoginResponse.Types.Code.AlreadyLogin, "", test.AccountId).ToArray()),
            }));
            existing.Send(NoticePacket.Disconnect(new Maple2.Model.Game.InterfaceText("probe")));
            Assert.That(other.TakeSent(), Is.Empty, "the other connection is cut");
        });
        existing.Dispose();
    }

    // The account is in game and the channel did not find the session to end: still the kick text, the error logged.
    [Test]
    public void InGame_DisconnectNotDone_StillAlreadyLogin() {
        test.World.Account = new() { AccountId = test.AccountId, CharacterId = test.CharacterId, Channel = 1 };
        test.World.DisconnectSucceeds = false;

        Login(2);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.EqualTo(new[] {
                Convert.ToHexString(LoginResultPacket.Error((byte) LoginResponse.Types.Code.AlreadyLogin, "", test.AccountId).ToArray()),
            }));
            Assert.That(test.World.Disconnects.Select(request => (request.CharacterId, request.Force)), Is.EqualTo(new[] { (test.CharacterId, true) }));
        });
    }
}

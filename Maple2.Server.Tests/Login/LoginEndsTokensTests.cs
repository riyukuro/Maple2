using System.Linq;
using Grpc.Core;
using Maple2.Server.Core.Constants;
using Maple2.Server.Global.Service;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using GlobalClient = Maple2.Server.Global.Service.Global.GlobalClient;

namespace Maple2.Server.Tests.Login;

// A login of the account ends the move tokens of the client it replaces. Before, a token that client held stayed
// live: it entered a game session after the kick (which looks only at sessions the World already has) and the new
// login's select then found nothing loaded yet to end, so both played.
public class LoginEndsTokensTests {
    private sealed class FakeGlobal(long accountId) : GlobalClient {
        public override LoginResponse Login(LoginRequest request, CallOptions options) {
            return new LoginResponse { Code = LoginResponse.Types.Code.Ok, AccountId = accountId };
        }
    }

    private TestLoginSession test = null!;
    private LoginHandler handler = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        handler = new LoginHandler { Global = new FakeGlobal(test.AccountId), World = test.World };
        session = test.Start(init: false);
        TestLoginSession.SetField(session, "name", "127.0.0.1:1");
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void Login(byte command) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteUnicodeString("user");
            writer.WriteUnicodeString("pass");
            writer.WriteShort(0);
            writer.WriteBytes(new byte[16]);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteString("");
        }));
    }

    [Test]
    public void ALogin_EndsTheAccountsTokens() {
        Login(2);

        Assert.That(test.World.EndedTokens, Is.EqualTo(new[] { test.AccountId }));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.CharacterList));
    }

    // The account is in game: the login kicks it, and the tokens end before the kick.
    [Test]
    public void ALoginThatKicks_EndsTheAccountsTokensToo() {
        test.World.Account = new() { AccountId = test.AccountId, CharacterId = test.CharacterId, Channel = 1 };

        Login(2);

        Assert.Multiple(() => {
            Assert.That(test.World.EndedTokens, Is.EqualTo(new[] { test.AccountId }));
            Assert.That(test.World.Disconnects.Select(request => request.CharacterId), Is.EqualTo(new[] { test.CharacterId }));
        });
    }
}

using Maple2.Server.Login.Service;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Tests.Login;

// What the web server's upload route asks the login server (Login.CharacterList): whether the account has an entered
// login session that is still on its character list, where the client uploads a new character's profile picture.
public class CharacterListServiceTests {
    [Test]
    public void OnCharacterList_EnteredSessionOnly() {
        var test = new TestLoginSession();
        LoginSession session = test.Start();
        try {
            Assert.That(LoginService.OnCharacterList(session.Server, test.AccountId), Is.True);
            Assert.That(LoginService.OnCharacterList(session.Server, test.AccountId + 1), Is.False);
            Assert.That(LoginService.OnCharacterList(session.Server, 0), Is.False);
            // Gone on to a game server or back to the server list: the account is no longer on its character list.
            session.LeaveCharacterList();
            Assert.That(LoginService.OnCharacterList(session.Server, test.AccountId), Is.False);
        } finally {
            session.Dispose();
        }
    }

    // A session whose key was not checked yet (no Init) is not entered.
    [Test]
    public void OnCharacterList_NotEntered() {
        var test = new TestLoginSession();
        LoginSession session = test.Start(init: false);
        try {
            Assert.That(LoginService.OnCharacterList(session.Server, test.AccountId), Is.False);
        } finally {
            session.Dispose();
        }
    }
}

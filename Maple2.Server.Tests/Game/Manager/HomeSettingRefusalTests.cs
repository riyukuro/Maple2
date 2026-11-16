using System;
using System.Linq;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A home message that is too long or a passcode that is neither "private" nor six characters is refused with
// s_ugcmap_center and changes nothing; a refused database write answers s_ugcmap_db.
public class HomeSettingRefusalTests {
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private UgcMapError Refusal() {
        byte[][] sent = test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.ResponseCube).ToArray();
        Assert.That(sent, Has.Length.EqualTo(1));
        return (UgcMapError) sent[0][3];
    }

    [Test]
    public void Message_TooLong_IsRefused() {
        GameSession session = Start();
        string before = session.Player.Value.Home.Message;

        session.Housing.SetHomeMessage(new string('a', 101));

        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_center));
        Assert.That(session.Player.Value.Home.Message, Is.EqualTo(before));
    }

    [TestCase("abc")]
    [TestCase("1234567")]
    [TestCase("priv")]
    public void Passcode_OfAnotherShape_IsRefused(string passcode) {
        GameSession session = Start();
        string before = session.Player.Value.Home.Passcode;

        session.Housing.SetPasscode(passcode);

        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_center));
        Assert.That(session.Player.Value.Home.Passcode, Is.EqualTo(before));
    }

    [Test]
    public void Message_WhenTheWriteFails_AnswersADatabaseError() {
        GameSession session = Start();
        test.FailSave = _ => true;

        session.Housing.SetHomeMessage("hello");

        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_db));
    }

    [Test]
    public void Passcode_WhenTheWriteFails_AnswersADatabaseError() {
        GameSession session = Start();
        test.FailSave = _ => true;

        session.Housing.SetPasscode("123456");

        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_db));
    }
}

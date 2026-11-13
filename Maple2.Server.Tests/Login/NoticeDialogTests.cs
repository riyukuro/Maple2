using Maple2.Server.Core.Constants;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Login;

public class NoticeDialogTests {
    // The login notice's packet (0x7a, mode 1) is read and answered with nothing; the session stays.
    [Test]
    public void TheLoginNoticeIsReadAndNotAnswered() {
        var test = new TestLoginSession();
        LoginSession session = test.Start();
        var handler = new NoticeDialogHandler();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));

        Assert.Multiple(() => {
            Assert.That(handler.OpCode, Is.EqualTo(RecvOp.NoticeDialog));
            Assert.That((ushort) handler.OpCode, Is.EqualTo(0x7A));
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
            Assert.That(session.Server.GetSession(test.AccountId, out _), Is.True);
        });
    }
}

using Maple2.Model.Enum;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ResponseFieldEnter is the client's one answer to a RequestFieldEnter (scenestategame.cpp:2284). Every field the
// server prepares sets ChangeMap and the entry sets Connected; an answer outside a field change is a replay and is
// dropped instead of running the arrival (the map condition, explore exp, the field's add) again.
public class FieldEnterReplayTests {
    private static (TestGameSession Test, GameSession Session) Connected() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000913));
        session.State = SessionState.Connected;
        test.TakeSent();
        return (test, session);
    }

    [Test]
    public void AReplayInTheField_DoesNothing() {
        (TestGameSession test, GameSession session) = Connected();
        Assert.DoesNotThrow(() => new FieldEnterHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteInt(GameSession.FIELD_KEY))));
        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.State, Is.EqualTo(SessionState.Connected));
        });
    }

    [Test]
    public void OnlyAFieldChange_IsEntered() {
        Assert.Multiple(() => {
            Assert.That(FieldEnterHandler.Accepts(SessionState.ChangeMap), Is.True);
            Assert.That(FieldEnterHandler.Accepts(SessionState.Connected), Is.False);
            Assert.That(FieldEnterHandler.Accepts(SessionState.ChangeChannel), Is.False);
            Assert.That(FieldEnterHandler.Accepts(SessionState.Disconnected), Is.False);
        });
    }
}

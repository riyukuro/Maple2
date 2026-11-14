using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A fare held with TryTakeMeso(notify: false) and given back on a refusal (the taxis' RideForMeso): silent when no meso
// packet went out meanwhile, since the client still shows the balance before the take; with the balance when one did
// (a gain on the field's thread), since that packet showed the balance without the fare.
public class HeldMesoReturnTests {
    private static (long Balance, int Sent) HoldAndReturn(bool sentMeanwhile) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meso = 1000;
        long sends = session.Currency.MesoSends;
        Assert.That(session.Currency.TryTakeMeso(300, notify: false), Is.True);
        if (sentMeanwhile) {
            session.Currency.NotifyMeso();
        }
        test.TakeSent();
        session.Currency.ReturnHeldMeso(300, sends);
        return (session.Currency.Meso, test.TakeSent().Count);
    }

    [Test]
    public void WithNoPacketMeanwhile_TheReturnIsSilent() {
        Assert.That(HoldAndReturn(false), Is.EqualTo((1000L, 0)));
    }

    [Test]
    public void AfterAPacketShowedTheBalanceWithoutTheFare_TheReturnSendsIt() {
        Assert.That(HoldAndReturn(true), Is.EqualTo((1000L, 1)));
    }
}

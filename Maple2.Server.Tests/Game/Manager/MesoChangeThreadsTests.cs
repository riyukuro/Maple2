using System.Threading;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A meso change on the field's thread (an arcade, dark stream, mini game or trigger reward) while the packet thread
// changes the balance too: each change is read and written in one step, so none is lost. Before, two changes that
// read the same balance wrote over each other (a payment undone or a gain lost).
public class MesoChangeThreadsTests {
    [Test]
    public void TwoThreads_NoChangeLost() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meso = 0;
        test.TakeSent();
        const int Count = 20000;
        using var start = new Barrier(2);
        void Gain() {
            start.SignalAndWait();
            for (int i = 0; i < Count; i++) {
                session.Currency.AddMeso(1, ExpMessageCode.s_msg_take_exp);
            }
        }
        var other = new Thread(Gain);
        other.Start();
        Gain();
        other.Join();

        Assert.That(session.Currency.Meso, Is.EqualTo(2L * Count));
    }
}

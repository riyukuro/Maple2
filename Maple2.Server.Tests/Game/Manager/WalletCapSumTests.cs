using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A gain to a balance near the cap: MaxMeso and MaxMeret are long.MaxValue, so balance + gain wrapped. AddMeso set
// the wrapped (negative) balance; ChangeMeso, ChangeMeret and ChangeGameMeret took it for a take of more than is held
// and threw, so the gain was lost. The gain now stops at the cap, as every gain does.
public class WalletCapSumTests {
    private GameSession Start() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meso = Constant.MaxMeso - 5;
        session.Currency.SetMeret(Constant.MaxMeret - 5, reportSpent: false);
        session.Currency.GameMeret = Constant.MaxMeret - 5;
        return session;
    }

    [Test]
    public void AddMeso_PastTheCap_StopsAtTheCap() {
        GameSession session = Start();

        session.Currency.AddMeso(10, ExpMessageCode.s_msg_take_exp);

        Assert.That(session.Currency.Meso, Is.EqualTo(Constant.MaxMeso));
    }

    [Test]
    public void ChangeMeso_PastTheCap_StopsAtTheCap() {
        GameSession session = Start();

        session.Currency.ChangeMeso(10);

        Assert.That(session.Currency.Meso, Is.EqualTo(Constant.MaxMeso));
    }

    [Test]
    public void ChangeMeret_PastTheCap_StopsAtTheCap() {
        GameSession session = Start();

        session.Currency.ChangeMeret(10);
        session.Currency.ChangeGameMeret(10);

        Assert.That((session.Currency.Meret, session.Currency.GameMeret), Is.EqualTo((Constant.MaxMeret, Constant.MaxMeret)));
    }

    // A take near the cap is still a take.
    [Test]
    public void ChangeMeso_TakeNearTheCap_Takes() {
        GameSession session = Start();

        session.Currency.ChangeMeso(-10);

        Assert.That(session.Currency.Meso, Is.EqualTo(Constant.MaxMeso - 15));
    }
}

using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// NpcScriptManager.TakeSalary: the pay of a maid's talk. A salary of type 0 is mesos, any other type merets
// (MaidTable.Entry); a talker who cannot pay, or whose session is gone, pays nothing.
public class MaidSalaryTests {
    private TestGameSession test = null!;
    private GameSession session = null!;

    private bool Take(MaidTable.Entry salary) {
        var script = (NpcScriptManager) RuntimeHelpers.GetUninitializedObject(typeof(NpcScriptManager));
        typeof(NpcScriptManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(script, session);
        return (bool) typeof(NpcScriptManager).GetMethod("TakeSalary", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(script, [salary])!;
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
        session.State = SessionState.Connected;
        session.Currency.Meso = 500;
        session.Currency.Meret = 80;
    }

    [Test]
    public void TypeZeroTakesMesosAndOtherTypesMerets() {
        Assert.Multiple(() => {
            Assert.That(Take(new MaidTable.Entry(1, 1, 1, SalaryType: 0, Salary: 300)), Is.True);
            Assert.That(session.Currency.Meso, Is.EqualTo(200));
            Assert.That(session.Currency.Meret, Is.EqualTo(80));
            Assert.That(Take(new MaidTable.Entry(1, 1, 1, SalaryType: 1, Salary: 50)), Is.True);
            Assert.That(session.Currency.Meret, Is.EqualTo(30));
            Assert.That(session.Currency.Meso, Is.EqualTo(200));
        });
    }

    [Test]
    public void ATalkerWhoCannotPayPaysNothing() {
        Assert.Multiple(() => {
            Assert.That(Take(new MaidTable.Entry(1, 1, 1, SalaryType: 0, Salary: 900)), Is.False);
            Assert.That(Take(new MaidTable.Entry(1, 1, 1, SalaryType: 1, Salary: 900)), Is.False);
            Assert.That(session.Currency.Meso, Is.EqualTo(500));
            Assert.That(session.Currency.Meret, Is.EqualTo(80));
        });
    }

    [Test]
    public void AGoneSessionPaysNothing() {
        session.State = SessionState.Disconnected;

        Assert.Multiple(() => {
            Assert.That(Take(new MaidTable.Entry(1, 1, 1, SalaryType: 0, Salary: 100)), Is.False);
            Assert.That(session.Currency.Meso, Is.EqualTo(500));
        });
    }
}

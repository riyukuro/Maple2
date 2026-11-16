using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The pet attack flag (RequestPet 15) goes with a move to another game server. The client keeps its own flag across
// the move (CPetManager is one for the process, its constructor alone sets m_nAuto), and the target's login sends
// AutoChanged from the session's flag: before, the move dropped the flag and that login sent on, so a player who
// had turned it off found the button on again.
public class PetAttackFlagMoveTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int Target = 2000062;

    private static GameSession Live(TestGameSession test, bool attackOff) {
        GameSession session = test.Start();
        test.SavingConfig(session);
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        session.State = SessionState.Connected;
        session.PetAttackOff = attackOff;
        test.TakeSent();
        return session;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ChannelChange_CarriesTheFlag(bool attackOff) {
        var test = new TestGameSession();
        GameSession session = Live(test, attackOff);

        new ChannelHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteShort(1)));

        Assert.That(test.World.Moves.Single().PetAttackOff, Is.EqualTo(attackOff));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FieldMove_CarriesTheFlag(bool attackOff) {
        var test = new TestGameSession();
        GameSession session = Live(test, attackOff);

        session.Migrate(Target);

        Assert.That(test.World.Moves.Single().PetAttackOff, Is.EqualTo(attackOff));
    }

    // The target's login sends AutoChanged with the flag the move carried (mode 15, then 1 for on).
    [TestCase(true, (byte) 0)]
    [TestCase(false, (byte) 1)]
    public void Login_SendsTheCarriedFlag(bool attackOff, byte sent) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        byte[] packet = session.TakeMigratedPet(new MigrateInResponse { PetUid = 7, PetAttackOff = attackOff }).ToArray();

        Assert.That(session.PetAttackOff, Is.EqualTo(attackOff));
        Assert.That(packet[^2..], Is.EqualTo(new byte[] { 15, sent }));
        Assert.That(session.SummonedPetUid, Is.EqualTo(7));
    }
}

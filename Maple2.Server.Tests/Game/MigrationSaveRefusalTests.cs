using System;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A move whose save does not store is not made (GameSession.MigrationSave): the client was sent to a server that
// loaded the last stored state, while the items made since were stored at once and the ones used up were not. The
// save is tried twice; a second failure ends the session, as a failed SessionSave does, and asks the World for no move.
public class MigrationSaveRefusalTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Target = 2000062;

    private static bool Ended(GameSession session) =>
        (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", Any)!.GetValue(session)! == 1;

    // A session whose saves run (TestGameSession marks the move's save as made).
    private static GameSession Live(TestGameSession test) {
        GameSession session = test.Start();
        test.SavingConfig(session);
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        session.State = SessionState.Connected;
        test.TakeSent();
        return session;
    }

    // Every save throws, or only the first `times` ones.
    private static void FailSaves(TestGameSession test, int times = int.MaxValue) {
        int failed = 0;
        test.FailSave = _ => failed++ < times;
    }

    [Test]
    public void FailedSave_IsFalse_AndEndsTheSession() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test);

        Assert.That(session.MigrationSave(), Is.False);
        Assert.Multiple(() => {
            Assert.That(session.MigrationSaved, Is.False, "the session's next save is made");
            Assert.That(session.DropsPackets, Is.False);
            Assert.That(Ended(session), Is.True);
        });
    }

    [Test]
    public void SaveRefusedOnce_SecondTryStores() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test, times: 1);

        Assert.That(session.MigrationSave(), Is.True);
        Assert.Multiple(() => {
            Assert.That(session.MigrationSaved, Is.True);
            Assert.That(Ended(session), Is.False);
        });
    }

    [Test]
    public void Migrate_FailedSave_NoMove() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        int mapBefore = session.Player.Value.Character.MapId;
        FailSaves(test);

        session.Migrate(Target);

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(session.Player.Value.Character.MapId, Is.EqualTo(mapBefore));
            Assert.That(session.State, Is.EqualTo(SessionState.Connected));
            Assert.That(session.MigrationSaved, Is.False);
        });
    }

    [Test]
    public void Migrate_SaveRefusedOnce_Moves() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test, times: 1);

        session.Migrate(Target);

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
    }

    [Test]
    public void ChannelChange_FailedSave_NoMove() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test);

        new ChannelHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteShort(1)));

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(session.State, Is.EqualTo(SessionState.Connected));
        });
    }

    [Test]
    public void QuitToCharacterList_FailedSave_NoMove() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test);

        new QuitHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteBool(false)));

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(Ended(session), Is.True);
        });
    }
}

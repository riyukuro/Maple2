using System;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game;

// The first tombstone, the one a pet's Revival 3 hits: a death while no death penalty count runs. The client clears
// its count when the server tick reaches the penalty's end tick (PC::FUN_141661ac0) and darkens the tombstone only for
// a count above 0 at death (PC::vfunc_137); the server's count stays until the next field entry, so a death after the
// end tick must still be the first tombstone.
public class FirstTombstoneTests {
    private const int Town = 2000001;
    private const long Now = 500_000;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        typeof(FieldManager).GetProperty(nameof(FieldManager.FieldTick))!.SetValue(session.Field, Now);
    }

    private void Die(short deathCount, long penaltyEndTick) {
        // A test session's ConfigManager has no session of its own (as in MesoRevivalEventTests).
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Config, session);
        session.Player.Value.Character.DeathCount = deathCount;
        session.Player.Value.Character.DeathTick = penaltyEndTick;
        typeof(FieldPlayer).GetProperty("IsDead")!.GetSetMethod(true)!.Invoke(session.Player, [true]);
    }

    [Test]
    public void NoPenalty_FirstTombstone() {
        Die(0, 0);
        Assert.That(session.Player.DeathState, Is.EqualTo(DeathState.FirstDeath));
    }

    [Test]
    public void PenaltyRunning_DarkTombstone() {
        Die(1, Now + 1);
        Assert.That(session.Player.DeathState, Is.EqualTo(DeathState.Metal));
    }

    [Test]
    public void PenaltyRanOutInThisField_FirstTombstoneAgain() {
        Die(2, Now);
        Assert.That(session.Player.DeathState, Is.EqualTo(DeathState.FirstDeath));
    }
}

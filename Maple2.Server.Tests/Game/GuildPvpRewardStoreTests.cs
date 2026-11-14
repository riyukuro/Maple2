using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The guild PvP fighter reward's daily count (GuildPvpZoneField.TakeStoredFighterReward) is in the database before
// the reward item's row is made: a crash after the item cannot give the day's count back. A count not stored is
// not taken and pays nothing.
public class GuildPvpRewardStoreTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Character Character => session.Player.Value.Character;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
    }

    private int StoredCount() {
        using var context = new Ms2Context(test.Options);
        return context.Character.AsNoTracking().Single(row => row.Id == session.CharacterId).Cooldown.GuildPvpRewardCount;
    }

    [Test]
    public void CountStoredBeforeTheReward() {
        var now = new DateTime(2026, 3, 1, 20, 0, 0);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Character>().Any(entry => entry.State == EntityState.Modified);

        Assert.That(GuildPvpZoneField.TakeStoredFighterReward(session.Player, true, true, now), Is.False);
        Assert.That((Character.GuildPvpRewardDay, Character.GuildPvpRewardCount), Is.EqualTo((0, 0)));
        test.FailSave = null;
        Assert.That(StoredCount(), Is.Zero);
        test.Errors.Clear();

        Assert.That(GuildPvpZoneField.TakeStoredFighterReward(session.Player, true, true, now), Is.True);
        Assert.That((Character.GuildPvpRewardCount, StoredCount()), Is.EqualTo((1, 1)));
    }

    // Not a fighter, or a guild the World did not pay: nothing is taken and nothing is stored.
    [Test]
    public void NoReward_StoresNothing() {
        var now = new DateTime(2026, 3, 1, 20, 0, 0);

        Assert.That(GuildPvpZoneField.TakeStoredFighterReward(session.Player, false, true, now), Is.False);
        Assert.That(GuildPvpZoneField.TakeStoredFighterReward(session.Player, true, false, now), Is.False);
        Assert.That((Character.GuildPvpRewardCount, StoredCount()), Is.EqualTo((0, 0)));
    }
}

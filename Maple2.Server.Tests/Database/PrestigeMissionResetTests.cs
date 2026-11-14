using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// The weekly reset starts the prestige missions of an offline account again: the SQL sets the column to its default,
// an empty list, and the next login makes the list again from the mission table (ExperienceManager). The reset is
// MySQL SQL: the test needs MS2_TEST_GAME_DB, a scratch MySQL game database.
public class PrestigeMissionResetTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private DbContextOptions options = null!;
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the reset is MySQL SQL: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        test = new TestGameSession(options);
        // KR adventurelevelmission 1-3: gain 1, 2 and 4 prestige levels.
        var missions = new Dictionary<int, PrestigeMissionMetadata>();
        foreach ((int id, int count, int box) in new[] { (1, 1, 20302615), (2, 2, 20302616), (3, 4, 20302617) }) {
            missions[id] = new PrestigeMissionMetadata(id, count, new ItemComponent(box, 4, 1, ItemTag.None));
        }
        test.SetTable(new PrestigeMissionTable(missions));
    }

    private IList<Maple2.Database.Model.PrestigeMission> Stored() {
        using var context = new Ms2Context(options);
        return context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).PrestigeMissions;
    }

    [Test]
    public void WeeklyReset_StartsAnOfflineAccountsMissionsAgain() {
        using (var context = new Ms2Context(options)) {
            Maple2.Database.Model.Account account = context.Account.Single(row => row.Id == test.AccountId);
            account.PrestigeMissions = [
                new Maple2.Database.Model.PrestigeMission { Id = 1, GainedLevels = 4, Awarded = true },
                new Maple2.Database.Model.PrestigeMission { Id = 2, GainedLevels = 4, Awarded = true },
                new Maple2.Database.Model.PrestigeMission { Id = 3, GainedLevels = 3, Awarded = false },
            ];
            context.SaveChanges();
        }
        Assume.That(Stored().Count(mission => mission.Awarded), Is.EqualTo(2));

        using (GameStorage.Request db = test.Storage.Context()) {
            db.WeeklyReset();
        }
        Assert.That(Stored(), Is.Empty);

        GameSession session = test.Start();
        Assert.That(session.Exp.PrestigeMissions.Select(mission => (mission.Id, mission.GainedLevels, mission.Awarded)),
            Is.EquivalentTo(new[] { (1L, 0L, false), (2L, 0L, false), (3L, 0L, false) }));
    }
}

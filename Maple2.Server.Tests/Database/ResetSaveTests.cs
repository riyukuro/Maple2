using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Tools.Scheduler;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// The World's daily reset sets the stored rows with raw SQL; an online session takes it in memory once, from the
// World's message or from its next save, which reads the reset's time before it writes. The save used to write the
// state from before the reset back over the reset rows (a death count or the day's prestige levels kept for good once
// the session ended). The reset is MySQL SQL: the tests need MS2_TEST_GAME_DB, a scratch MySQL game database (the
// reset runs over all of its rows).
public class ResetSaveTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private DbContextOptions options = null!;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the reset is MySQL SQL: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        test = new TestGameSession(options);
        test.Start();
        // The managers a full save writes, as SessionSaveTransactionTests sets them, and the queue the reset uses.
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Config = new ConfigManager(db, session);
        }
        session.Survival = new SurvivalManager(session);
        typeof(GameSession).GetField(nameof(GameSession.Scheduler))!.SetValue(session, new EventQueue(Serilog.Log.Logger));
        // The first save: the session holds the resets stored up to now.
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(test.Errors, Is.Empty);
    }

    private (int Levels, int Revivals) Stored() {
        using var context = new Ms2Context(options);
        int levels = context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).PrestigeLevelsGained;
        int revivals = context.CharacterConfig.AsNoTracking().Single(config => config.CharacterId == test.CharacterId).InstantRevivalCount;
        return (levels, revivals);
    }

    private void Played() {
        session.Player.Value.Account.PrestigeLevelsGained = 3;
        session.Config.InstantReviveCount = 2;
    }

    private void WorldDailyReset() {
        using GameStorage.Request db = test.Storage.Context();
        db.DailyReset();
    }

    // A save after the reset, before the World's message: the session takes the reset and stores it.
    [Test]
    public void SaveAfterTheReset_StoresTheReset() {
        Played();
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(Stored(), Is.EqualTo((3, 2)));

        WorldDailyReset();
        Assert.That(session.SessionSave(), Is.True);
        Assert.Multiple(() => {
            Assert.That(Stored(), Is.EqualTo((0, 0)));
            Assert.That((session.Player.Value.Account.PrestigeLevelsGained, session.Config.InstantReviveCount), Is.EqualTo((0, 0)));
        });
    }

    // The World's message after a save took the reset does not reset the session again: what it did since stays.
    [Test]
    public void MessageAfterTheSave_DoesNotResetAgain() {
        WorldDailyReset();
        Assume.That(session.SessionSave(), Is.True);
        Played();

        GameStorage.ResetStamps stamps;
        using (GameStorage.Request db = test.Storage.Context()) {
            stamps = db.GetResetStamps();
        }
        session.TakeResets(stamps);
        Assert.That((session.Player.Value.Account.PrestigeLevelsGained, session.Config.InstantReviveCount), Is.EqualTo((3, 2)));
        Assert.That(session.SessionSave(), Is.True);
        Assert.That(Stored(), Is.EqualTo((3, 2)));
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Server.Tests.Game;
using Maple2.Server.World;
using Maple2.Server.World.Containers;
using Maple2.Tools.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Database;

// The World's reset when a try's commit is stored but its answer is lost (the connection cut after the commit): the
// try again, RunReset's own or the World's 5 min later, finds the reset stored and does not run it again. It used to
// run it twice, so what the players did in between (the day's revivals, clears, listings) was reset a second time.
// MySQL SQL: the tests need MS2_TEST_GAME_DB.
public class ResetRetryTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private readonly LostCommit lost = new();
    private readonly CountRuns runs = new();
    private TestGameSession test = null!;
    private WorldServer world = null!;
    private EventQueue scheduler = null!;
    private int told;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the reset is MySQL SQL: set MS2_TEST_GAME_DB");
        }
        lost.Lose = 0;
        runs.Count = 0;
        runs.RefuseStamp = false;
        told = 0;
        DbContextOptions options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection))
            .AddInterceptors(lost, runs).Options;
        test = new TestGameSession(options);
        world = (WorldServer) RuntimeHelpers.GetUninitializedObject(typeof(WorldServer));
        scheduler = new EventQueue(Serilog.Log.Logger);
        void Set(string name, object value) => typeof(WorldServer).GetField(name, Any)!.SetValue(world, value);
        Set("gameStorage", test.Storage);
        Set("channelClients", new ChannelClientLookup());
        Set("scheduler", scheduler);
        Set("logger", Serilog.Log.ForContext<WorldServer>());
        Set("resetRuns", new Dictionary<string, int>());
    }

    private void DailyReset() {
        Action<GameStorage.Request, DateTime> reset = (db, before) => db.DailyReset(before);
        Action stored = () => told++;
        typeof(WorldServer).GetMethod("GuardedReset", Any)!.Invoke(world, ["DailyReset", reset, stored]);
    }

    private DateTime Stamp() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetResetStamp("DailyReset");
    }

    // RunReset's own try again: the first commit's answer is lost, the second try finds the reset stored.
    [Test]
    public void LostCommit_RunResetTryAgain_DoesNotRunTwice() {
        DateTime before = Stamp();
        lost.Lose = 1;
        DailyReset();
        Assert.Multiple(() => {
            Assert.That(runs.Count, Is.EqualTo(1), "the reset's statements ran once");
            Assert.That(Stamp(), Is.GreaterThan(before));
            Assert.That((told, scheduler.Count), Is.EqualTo((1, 0)), "stored: the channels told, no try again");
        });
    }

    // The World's try again 5 min later: the commit's answer is lost and RunReset's other tries cannot read the
    // time (the database gone), so the World tries again later; that try finds the reset stored and only tells.
    [Test]
    public void LostCommit_WorldTryAgain_DoesNotRunTwice() {
        lost.Lose = 1;
        lost.OnLost = () => runs.RefuseStamp = true;
        DailyReset();
        Assume.That((runs.Count, told, scheduler.Count), Is.EqualTo((1, 0, 1)), "the World tries again later");

        runs.RefuseStamp = false;
        TryAgain();
        Assert.Multiple(() => {
            Assert.That(runs.Count, Is.EqualTo(1), "the reset's statements ran once");
            Assert.That(told, Is.EqualTo(1), "the try again tells the channels");
        });
    }

    // Runs the World's queued try again now (it waits 5 min).
    private void TryAgain() {
        var timed = (IList) typeof(EventQueue).GetField("timedEvents", Any)!.GetValue(scheduler)!;
        object retry = timed.Cast<object>().Single();
        var task = (Action) retry.GetType().GetField("task", Any)!.GetValue(retry)!;
        timed.Clear();
        task();
    }

    // Throws after a commit, as a connection cut before the commit's answer arrives.
    private sealed class LostCommit : DbTransactionInterceptor {
        public int Lose;
        public Action? OnLost;

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) {
            if (Lose > 0) {
                Lose--;
                OnLost?.Invoke();
                throw new InvalidOperationException("the test lost the commit's answer");
            }
        }
    }

    // Counts the daily reset's runs (its revival statement) and, while RefuseStamp, fails the reads of its time.
    private sealed class CountRuns : DbCommandInterceptor {
        public int Count;
        public bool RefuseStamp;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) {
            if (command.CommandText.Contains("`InstantRevivalCount` = 0")) {
                Count++;
            }
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) {
            if (RefuseStamp && command.CommandText.Contains("server-info")) {
                throw new InvalidOperationException("the test refused the read");
            }
            return result;
        }
    }
}

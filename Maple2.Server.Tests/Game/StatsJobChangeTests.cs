using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// StatsManager.Refresh builds a player's base stats from the level table of the player's current job: after a job
// change (the GM job command) it is the new job's table, not the one picked at session start.
public class StatsJobChangeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;

    private static IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>> Table(long spRegen, long interval) =>
        new Dictionary<short, IReadOnlyDictionary<BasicAttribute, long>> {
            { 60, new Dictionary<BasicAttribute, long> { { BasicAttribute.SpRegen, spRegen }, { BasicAttribute.SpRegenInterval, interval } } },
        };

    private static StatsManager Manager(JobCode start) {
        var jobs = new Dictionary<JobCode, IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>> {
            { JobCode.HeavyGunner, Table(0, 10000) },
            { JobCode.Priest, Table(1, 1000) },
        };
        var manager = (StatsManager) RuntimeHelpers.GetUninitializedObject(typeof(StatsManager));
        typeof(StatsManager).GetField("jobStats", Any)!.SetValue(manager, jobs);
        typeof(StatsManager).GetField("levelStats", Any)!.SetValue(manager, jobs[start]);
        typeof(StatsManager).GetField("levelStatsJob", Any)!.SetValue(manager, start);
        return manager;
    }

    [Test]
    public void JobChange_PicksTheNewJobsTable() {
        StatsManager manager = Manager(JobCode.HeavyGunner);
        Assert.That(manager.LevelStats(JobCode.HeavyGunner)[60][BasicAttribute.SpRegenInterval], Is.EqualTo(10000));
        Assert.That(manager.LevelStats(JobCode.Priest)[60][BasicAttribute.SpRegen], Is.EqualTo(1));
        Assert.That(manager.LevelStats(JobCode.Priest)[60][BasicAttribute.SpRegenInterval], Is.EqualTo(1000));
        Assert.That(manager.LevelStats(JobCode.HeavyGunner)[60][BasicAttribute.SpRegen], Is.EqualTo(0));
    }

    // A job with no table keeps the last one (Refresh then logs the missing level as before).
    [Test]
    public void UnknownJob_KeepsTheTable() {
        StatsManager manager = Manager(JobCode.Priest);
        Assert.That(manager.LevelStats(JobCode.Knight)[60][BasicAttribute.SpRegen], Is.EqualTo(1));
    }
}

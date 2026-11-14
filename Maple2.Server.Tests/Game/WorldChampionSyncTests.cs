using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// The world champion npc of a season's field: it shows the step and health the World server answers its damage
// reports. Made-up season of two steps on field 2000423, spawn point 100.
public class WorldChampionSyncTests {
    private static readonly DateTime Begin = new(2026, 11, 13, 0, 0, 0);
    private static readonly WorldChampionTable Table = new(new Dictionary<int, WorldChampionTable.Season> {
        [1] = new(1, 2000423, 100, Begin, Begin.AddDays(3).AddSeconds(-1), 1500, 180510006, [
            new WorldChampionTable.Step(1, 23500110, 20000001),
            new WorldChampionTable.Step(2, 23500111, 20000002),
        ]),
    });

    private static readonly FeatureTable On = new(new Dictionary<string, int> { [WorldChampionRules.Feature] = 1 });

    private sealed class Host : WorldChampionSync.IHost {
        public readonly List<string> Calls = [];
        public bool SpawnFails;
        private int nextId = 10;

        public (int ObjectId, long MaxHealth)? Spawn(int npcId, int spawnPointId) {
            Calls.Add($"spawn {npcId} at {spawnPointId}");
            return SpawnFails ? null : (++nextId, 1000);
        }

        public void Remove(int objectId) => Calls.Add($"remove {objectId}");
        public void SetHealth(int objectId, long health) => Calls.Add($"health {objectId} {health}");
    }

    // Season 1 as above and season 2 on another map (2000062) at the same time.
    private static readonly WorldChampionTable TwoMaps = new(new Dictionary<int, WorldChampionTable.Season> {
        [1] = Table.Entries[1],
        [2] = new(2, 2000062, 100, Begin, Begin.AddDays(3).AddSeconds(-1), 1500, 180510006, [
            new WorldChampionTable.Step(1, 23500120, 20000001),
        ]),
    });

    private const long Sender = 5;

    private DateTime now;
    private WorldChampionTable table = Table;
    private Host host = null!;
    private readonly List<(int Season, int Step, long MaxHealth, List<WorldChampionDamage> Damages, long Sender, long Sequence)> reports = [];
    private (int, int, long)? answer;
    private Action? held;
    private bool hold;

    private WorldChampionSync Sync(int mapId = 2000423) {
        return new WorldChampionSync(() => table, () => now, mapId, Sender, host, (season, step, maxHealth, damages, sender, sequence) => {
            reports.Add((season, step, maxHealth, damages.ToList(), sender, sequence));
            return answer;
        }, run => {
            if (hold) {
                held = run;
            } else {
                run();
            }
        }, run => run());
    }

    [SetUp]
    public void SetUp() {
        now = Begin.AddHours(1);
        table = Table;
        host = new Host();
        reports.Clear();
        answer = (1, 1, 0);
        held = null;
        hold = false;
    }

    [Test]
    public void Runs_OnTheSeasonsField() {
        Assert.Multiple(() => {
            Assert.That(WorldChampionSync.Runs(Table, On, 2000423), Is.True);
            Assert.That(WorldChampionSync.Runs(Table, On, 2000062), Is.False);
            // The WorldChampionMushroom feature off: no champion.
            Assert.That(WorldChampionSync.Runs(Table, new FeatureTable(new Dictionary<string, int>()), 2000423), Is.False);
        });
    }

    // No season on: no World call, no npc.
    [Test]
    public void NoSeason_NoCall() {
        now = Begin.AddSeconds(-1);
        WorldChampionSync sync = Sync();
        sync.Tick();
        Assert.Multiple(() => {
            Assert.That(reports, Is.Empty);
            Assert.That(host.Calls, Is.Empty);
        });
    }

    // The first answer spawns the step's npc at the season's spawn point, with the health the channels left it.
    [Test]
    public void SpawnsTheStepsNpc() {
        answer = (1, 1, 300);
        WorldChampionSync sync = Sync();
        sync.Tick();
        Assert.Multiple(() => {
            Assert.That((reports.Single().Season, reports.Single().Damages.Count), Is.EqualTo((0, 0)));
            Assert.That(host.Calls, Is.EqualTo(new[] { "spawn 23500110 at 100", "health 11 700" }));
            Assert.That((sync.Season, sync.Step, sync.ObjectId), Is.EqualTo((1, 1, 11)));
        });
    }

    // The players' damage is reported per character with the step and the npc's health; damage to another npc is not.
    [Test]
    public void ReportsTheDamage() {
        WorldChampionSync sync = Sync();
        sync.Tick();
        sync.Damaged(11, 107, 7, 100);
        sync.Damaged(11, 107, 7, 50);
        sync.Damaged(11, 108, 8, 20);
        sync.Damaged(99, 107, 7, 1000);
        answer = (1, 1, 170);
        sync.Tick();
        Assert.Multiple(() => {
            Assert.That((reports[1].Season, reports[1].Step, reports[1].MaxHealth), Is.EqualTo((1, 1, 1000L)));
            Assert.That(reports[1].Damages, Is.EquivalentTo(new[] { new WorldChampionDamage(107, 7, 150), new WorldChampionDamage(108, 8, 20) }));
            Assert.That(host.Calls[^1], Is.EqualTo("health 11 830"));
        });
    }

    // Damage dealt while a report is out shows in the health of its answer, and goes in the next report.
    [Test]
    public void DamageDuringAReport() {
        WorldChampionSync sync = Sync();
        sync.Tick();
        sync.Damaged(11, 107, 7, 100);
        hold = true;
        answer = (1, 1, 100);
        sync.Tick();
        sync.Damaged(11, 107, 7, 40);
        sync.Tick();
        // One report out at a time.
        Assert.That(held, Is.Not.Null);
        held!();
        Assert.That(reports, Has.Count.EqualTo(2));
        Assert.That(host.Calls[^1], Is.EqualTo("health 11 860"));
        hold = false;
        answer = (1, 1, 140);
        sync.Tick();
        Assert.That(reports[2].Damages, Is.EqualTo(new[] { new WorldChampionDamage(107, 7, 40) }));
    }

    // Another channel cleared the step: the npc is swapped for the next step's (removed, not killed); the damage
    // dealt to the old npc is still reported with its step.
    [Test]
    public void StepClearedSwapsTheNpc() {
        WorldChampionSync sync = Sync();
        sync.Tick();
        hold = true;
        sync.Tick();
        sync.Damaged(11, 107, 7, 60);
        answer = (1, 2, 0);
        held!();
        hold = false;
        Assert.That(host.Calls.Skip(2), Is.EqualTo(new[] { "remove 11", "spawn 23500111 at 100", "health 12 1000" }));
        sync.Damaged(12, 107, 7, 30);
        sync.Tick();
        Assert.That((reports[2].Step, reports[2].Damages.Single().Damage), Is.EqualTo((1, 60L)));
        answer = (1, 2, 30);
        sync.Tick();
        Assert.Multiple(() => {
            Assert.That((reports[3].Step, reports[3].Damages.Single().Damage), Is.EqualTo((2, 30L)));
            Assert.That(host.Calls[^1], Is.EqualTo("health 12 970"));
        });
    }

    // The season cleared or no longer on: the npc goes.
    [Test]
    public void SeasonClearedRemovesTheNpc() {
        WorldChampionSync sync = Sync();
        sync.Tick();
        answer = (1, 3, 0);
        sync.Tick();
        Assert.That(host.Calls[^1], Is.EqualTo("remove 11"));
        Assert.That(sync.ObjectId, Is.Zero);

        sync = Sync();
        answer = (1, 1, 0);
        sync.Tick();
        answer = (0, 0, 0);
        sync.Tick();
        Assert.That(host.Calls[^1], Is.EqualTo("remove 12"));
    }

    // A failed call: the damage is reported again at the next tick.
    [Test]
    public void FailedReportIsSentAgain() {
        WorldChampionSync sync = Sync();
        sync.Tick();
        sync.Damaged(11, 107, 7, 100);
        answer = null;
        sync.Tick();
        answer = (1, 1, 100);
        sync.Tick();
        Assert.That(reports[2].Damages, Is.EqualTo(new[] { new WorldChampionDamage(107, 7, 100) }));
    }

    // The batch sent again carries its first sequence (so the World server counts it once); the next batch the next
    // one; a report without damage none. Every report carries the field's sender id.
    [Test]
    public void SentAgainWithTheSameSequence() {
        WorldChampionSync sync = Sync();
        sync.Tick();
        sync.Damaged(11, 107, 7, 100);
        answer = null;
        sync.Tick();
        answer = (1, 1, 100);
        sync.Tick();
        sync.Damaged(11, 107, 7, 20);
        sync.Tick();
        sync.Tick();
        Assert.Multiple(() => {
            Assert.That(reports.Select(report => report.Sequence), Is.EqualTo(new long[] { 0, 1, 1, 2, 0 }));
            Assert.That(reports.Select(report => report.Sender), Is.All.EqualTo(Sender));
        });
    }

    // A hit between a failed answer and the resend goes in a new batch with its own sequence: in the batch sent
    // again it would carry the old sequence, and the World server (which may have applied the first send) would skip
    // it with the resend.
    [Test]
    public void HitAfterAFailedAnswerGetsItsOwnSequence() {
        WorldChampionSync sync = Sync();
        sync.Tick();
        sync.Damaged(11, 107, 7, 100);
        answer = null;
        sync.Tick();
        sync.Damaged(11, 107, 7, 20);
        answer = (1, 1, 100);
        sync.Tick();
        sync.Tick();
        Assert.Multiple(() => {
            Assert.That(reports.Select(report => report.Sequence), Is.EqualTo(new long[] { 0, 1, 1, 2 }));
            Assert.That(reports[2].Damages, Is.EqualTo(new[] { new WorldChampionDamage(107, 7, 100) }));
            Assert.That(reports[3].Damages, Is.EqualTo(new[] { new WorldChampionDamage(107, 7, 20) }));
        });
    }

    // Two seasons on at once on different maps: a field shows only its map's season. Answered the other map's season,
    // it shows no npc; with only another map's season on, it makes no call.
    [Test]
    public void OnlyTheFieldsMapsSeason() {
        table = TwoMaps;
        answer = (2, 1, 0);
        WorldChampionSync sync = Sync();
        sync.Tick();
        Assert.Multiple(() => {
            Assert.That(host.Calls, Is.Empty);
            Assert.That(sync.ObjectId, Is.Zero);
        });

        // The npc of season 1 shown, then the World server answers season 2: the npc goes.
        answer = (1, 1, 0);
        sync = Sync();
        sync.Tick();
        answer = (2, 1, 0);
        sync.Tick();
        Assert.That(host.Calls, Is.EqualTo(new[] { "spawn 23500110 at 100", "health 11 1000", "remove 11" }));

        // Season 1 over (only season 2 of map 2000062 left on): a field of 2000423 with nothing shown makes no call.
        table = new WorldChampionTable(new Dictionary<int, WorldChampionTable.Season> { [2] = TwoMaps.Entries[2] });
        reports.Clear();
        Sync().Tick();
        Assert.That(reports, Is.Empty);
    }

    // A spawn point the field does not have: tried once per step, not every second.
    [Test]
    public void SpawnFailsOnce() {
        host.SpawnFails = true;
        WorldChampionSync sync = Sync();
        sync.Tick();
        sync.Tick();
        Assert.That(host.Calls, Is.EqualTo(new[] { "spawn 23500110 at 100" }));
        answer = (1, 2, 0);
        sync.Tick();
        Assert.That(host.Calls, Has.Count.EqualTo(2));
    }
}

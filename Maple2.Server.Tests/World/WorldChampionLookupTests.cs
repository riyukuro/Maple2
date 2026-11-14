using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The World server's world champion state: the fields of all channels report their damage to the step's npc and are
// answered the step that is out and its damage. Made-up season of three steps.
public class WorldChampionLookupTests {
    private static readonly DateTime Begin = new(2026, 11, 13, 0, 0, 0);
    private static readonly WorldChampionTable Table = new(new Dictionary<int, WorldChampionTable.Season> {
        [1] = new(1, 2000423, 100, Begin, Begin.AddDays(3).AddSeconds(-1), 1500, 180510006, [
            new WorldChampionTable.Step(1, 23500110, 20000001),
            new WorldChampionTable.Step(2, 23500111, 20000002),
            new WorldChampionTable.Step(3, 23500112, 20000003),
        ]),
    });

    private DateTime now;
    private readonly List<(int Season, int Step, long StepDamage, List<WorldChampionDamage> Damages)> saves = [];
    private (int, long, List<WorldChampionDamage>)? loaded;
    private bool saveFails;

    private WorldChampionLookup Lookup() {
        return new WorldChampionLookup(() => Table, () => now, _ => loaded, (season, step, stepDamage, damages) => {
            if (saveFails) {
                return false;
            }
            saves.Add((season, step, stepDamage, damages.ToList()));
            return true;
        });
    }

    [SetUp]
    public void SetUp() {
        now = Begin.AddHours(1);
        saves.Clear();
        loaded = null;
        saveFails = false;
    }

    private static WorldChampionDamage[] Hit(long characterId, long damage) => [new WorldChampionDamage(characterId + 100, characterId, damage)];

    [Test]
    public void NoSeasonOn() {
        now = Begin.AddSeconds(-1);
        Assert.That(Lookup().Report(1, 1, 1000, Hit(7, 500)), Is.EqualTo((0, 0, 0L)));
    }

    // Two channels' damage adds up; the step is cleared when it reaches the npc's health.
    [Test]
    public void ChannelsAddUp() {
        WorldChampionLookup lookup = Lookup();
        Assert.Multiple(() => {
            Assert.That(lookup.Get(), Is.EqualTo((1, 1, 0L)));
            Assert.That(lookup.Report(1, 1, 1000, Hit(7, 400)), Is.EqualTo((1, 1, 400L)));
            Assert.That(lookup.Report(1, 1, 1000, Hit(8, 500)), Is.EqualTo((1, 1, 900L)));
            Assert.That(lookup.Report(1, 1, 1000, Hit(7, 200)), Is.EqualTo((1, 2, 0L)));
            Assert.That(lookup.DamageOf(1, 7), Is.EqualTo(600));
            Assert.That(lookup.DamageOf(1, 8), Is.EqualTo(500));
        });
    }

    // A channel that still shows the cleared step: its damage counts for the characters, not for the next step.
    [Test]
    public void LateDamageToAClearedStep() {
        WorldChampionLookup lookup = Lookup();
        lookup.Report(1, 1, 1000, Hit(7, 1000));
        Assert.Multiple(() => {
            Assert.That(lookup.Report(1, 1, 1000, Hit(8, 300)), Is.EqualTo((1, 2, 0L)));
            Assert.That(lookup.DamageOf(1, 8), Is.EqualTo(300));
        });
    }

    // Damage of a season that is not the one on is dropped; a cleared season takes no more damage.
    [Test]
    public void WrongSeasonAndCleared() {
        WorldChampionLookup lookup = Lookup();
        Assert.That(lookup.Report(2, 1, 1000, Hit(7, 400)), Is.EqualTo((1, 1, 0L)));
        lookup.Report(1, 1, 1000, Hit(7, 1000));
        lookup.Report(1, 2, 1000, Hit(7, 1000));
        Assert.That(lookup.Report(1, 3, 1000, Hit(7, 1000)), Is.EqualTo((1, 4, 0L)));
        Assert.Multiple(() => {
            Assert.That(lookup.Report(1, 4, 1000, Hit(7, 1000)), Is.EqualTo((1, 4, 0L)));
            Assert.That(lookup.DamageOf(1, 7), Is.EqualTo(3000));
        });
    }

    // The saved state is read back when the World server starts again.
    [Test]
    public void LoadsTheSavedState() {
        loaded = (2, 300, [new WorldChampionDamage(107, 7, 2300)]);
        WorldChampionLookup lookup = Lookup();
        Assert.Multiple(() => {
            Assert.That(lookup.Report(1, 2, 1000, Hit(7, 100)), Is.EqualTo((1, 2, 400L)));
            Assert.That(lookup.DamageOf(1, 7), Is.EqualTo(2400));
        });
    }

    // A save writes the totals of the characters that changed; nothing changed, nothing is saved; a failed save is
    // written by the next one.
    [Test]
    public void FlushWritesTheChanges() {
        WorldChampionLookup lookup = Lookup();
        lookup.Report(1, 1, 1000, Hit(7, 100));
        lookup.Report(1, 1, 1000, Hit(7, 50));
        lookup.Flush();
        lookup.Flush();
        Assert.That(saves, Has.Count.EqualTo(1));
        Assert.That(saves[0].Damages, Is.EqualTo(new[] { new WorldChampionDamage(107, 7, 150) }));
        Assert.That((saves[0].Step, saves[0].StepDamage), Is.EqualTo((1, 150L)));

        saveFails = true;
        lookup.Report(1, 1, 1000, Hit(8, 10));
        lookup.Flush();
        saveFails = false;
        lookup.Report(1, 1, 1000, Hit(7, 5));
        lookup.Flush();
        Assert.That(saves, Has.Count.EqualTo(2));
        Assert.That(saves[1].Damages, Is.EquivalentTo(new[] { new WorldChampionDamage(107, 7, 155), new WorldChampionDamage(108, 8, 10) }));
        Assert.That(saves[1].StepDamage, Is.EqualTo(165));
    }
}

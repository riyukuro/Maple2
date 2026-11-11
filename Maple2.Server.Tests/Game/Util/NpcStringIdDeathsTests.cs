using System;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// 02020051_bf's 104_end_1 waits on npc_is_dead_by_string_id string_id="Gigantika_01" or the timer's End=3.
public class NpcStringIdDeathsTests {
    private const string Boss = "Gigantika_01";

    [Test]
    public void NoDeathIsNotDead() {
        var deaths = new NpcStringIdDeaths();
        Assert.Multiple(() => {
            Assert.That(deaths.IsDead(Boss, [(Boss, false)]), Is.False);
            // Before the boss spawns (or after a destroy_monster) the field holds none: still not dead.
            Assert.That(deaths.IsDead(Boss, Array.Empty<(string?, bool)>()), Is.False);
        });
    }

    [Test]
    public void DeadBossHolds() {
        var deaths = new NpcStringIdDeaths();
        deaths.Died(Boss);
        Assert.Multiple(() => {
            Assert.That(deaths.IsDead(Boss, [(Boss, true), (null, false)]), Is.True);
            // The corpse has left Field.Mobs.
            Assert.That(deaths.IsDead(Boss, Array.Empty<(string?, bool)>()), Is.True);
            Assert.That(deaths.IsDead("gigantika_01", [(null, false)]), Is.True);
        });
    }

    [Test]
    public void LivingVariantKeepsItOpen() {
        var deaths = new NpcStringIdDeaths();
        deaths.Died(Boss);
        Assert.That(deaths.IsDead(Boss, [(Boss, true), (Boss, false)]), Is.False);
    }

    [Test]
    public void OtherStringIdDoesNotCount() {
        var deaths = new NpcStringIdDeaths();
        deaths.Died("Gigantika_02");
        Assert.That(deaths.IsDead(Boss, [("Gigantika_02", true)]), Is.False);
    }

    // A row ingested before the string id was kept, or a script without string_id.
    [Test]
    public void EmptyIdsNeverCount() {
        var deaths = new NpcStringIdDeaths();
        deaths.Died(null);
        deaths.Died("");
        Assert.Multiple(() => {
            Assert.That(deaths.IsDead("", [(null, true)]), Is.False);
            Assert.That(deaths.IsDead(Boss, [(null, true)]), Is.False);
        });
    }
}

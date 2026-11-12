using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// set_ladder skips a missing id and keeps going; spawn_monster finds plain spawn points too; init_npc_rotation
// takes the spawn point's rotation. Made-up ids.
public class TriggerLookupTests {
    [Test]
    public void SetLadder_SkipsAMissingIdOnly() {
        var ladders = new Dictionary<int, string> { [1] = "a", [3] = "c" };
        Assert.That(TriggerContext.PresentObjects(ladders, [1, 2, 3]).ToArray(), Is.EqualTo(new[] { "a", "c" }));
    }

    [Test]
    public void SpawnMonster_FindsEventThenPlainSpawnPoints() {
        var list = new List<SpawnPointNPCListEntry> { new(21000001, 2) };
        var eventSpawn = new EventSpawnPointNPC("e", 101, Vector3.Zero, Vector3.Zero, true, false, 0, 1, list, 0, 0, "");
        var plainSpawn = new SpawnPointNPC("p", 102, Vector3.Zero, Vector3.Zero, true, false, 0, list, 0, null);
        var events = new Dictionary<int, EventSpawnPointNPC> { [101] = eventSpawn };
        SpawnPointNPC[] plain = [plainSpawn];

        Assert.That(TriggerContext.FindSpawnPoint(events, plain, 101), Is.SameAs(eventSpawn));
        Assert.That(TriggerContext.FindSpawnPoint(events, plain, 102), Is.SameAs(plainSpawn));
        Assert.That(TriggerContext.FindSpawnPoint(events, plain, 103), Is.Null);
    }

    // '!monster_dead' (02000471 respawn): the ingest writes negate="true", which the condition now reads.
    [Test]
    public void MonsterDead_ReadsNegate() {
        Assert.That(TriggerContext.AllDead([true, true], false), Is.True);
        Assert.That(TriggerContext.AllDead([true, false], false), Is.False);
        Assert.That(TriggerContext.AllDead([true, true], true), Is.False);
        Assert.That(TriggerContext.AllDead([true, false], true), Is.True);
        Assert.That(TriggerContext.AllDead([], false), Is.True);
    }

    // 'monster_in_combat' (02000066 spawn_ids="5001"): a monster of a listed spawn point that is in battle.
    [Test]
    public void MonsterInCombat_LooksAtTheListedSpawnPoints() {
        (int, bool)[] mobs = [(5001, true), (5002, false), (0, true)];
        Assert.Multiple(() => {
            Assert.That(TriggerContext.AnyInCombat(mobs, [5001], false), Is.True);
            Assert.That(TriggerContext.AnyInCombat(mobs, [5002], false), Is.False);
            Assert.That(TriggerContext.AnyInCombat(mobs, [5002, 5001], false), Is.True);
            Assert.That(TriggerContext.AnyInCombat(mobs, [5003], false), Is.False);
            Assert.That(TriggerContext.AnyInCombat(mobs, [5001], true), Is.False);
            Assert.That(TriggerContext.AnyInCombat(mobs, [5002], true), Is.True);
            Assert.That(TriggerContext.AnyInCombat([], [5001], false), Is.False);
        });
    }

    [Test]
    public void InitNpcRotation_UsesTheSpawnPointRotation() {
        var list = new List<SpawnPointNPCListEntry> { new(21000001, 1) };
        var eventSpawn = new EventSpawnPointNPC("e", 101, Vector3.Zero, new Vector3(0, 0, 45), true, false, 0, 1, list, 0, 0, "");
        var plainSpawn = new SpawnPointNPC("p", 102, Vector3.Zero, new Vector3(0, 0, -90), true, false, 0, list, 0, null);
        var events = new Dictionary<int, EventSpawnPointNPC> { [101] = eventSpawn };
        SpawnPointNPC[] plain = [plainSpawn];

        Assert.That(TriggerContext.SpawnRotation(events, plain, 101), Is.EqualTo(new Vector3(0, 0, 45)));
        Assert.That(TriggerContext.SpawnRotation(events, plain, 102), Is.EqualTo(new Vector3(0, 0, -90)));
        Assert.That(TriggerContext.SpawnRotation(events, plain, 103), Is.Null);
    }

    [Test]
    public void LimitSpawnNpcCount_StopsMonsterSpawnsAtTheLimit() {
        // limit_spawn_npc_count limit_count=200: spawning stops once 200 monsters are up.
        Assert.That(TriggerContext.CanSpawnUnderLimit(200, 199), Is.True);
        Assert.That(TriggerContext.CanSpawnUnderLimit(200, 200), Is.False);
        Assert.That(TriggerContext.CanSpawnUnderLimit(200, 250), Is.False);
        // limit_count=0 turns the limit off.
        Assert.That(TriggerContext.CanSpawnUnderLimit(0, 5000), Is.True);
    }
}

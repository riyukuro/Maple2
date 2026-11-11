using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// set_ladder skips a missing id and keeps going; spawn_monster finds plain spawn points too. Made-up ids.
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
}

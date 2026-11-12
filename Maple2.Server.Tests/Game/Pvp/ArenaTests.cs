using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game.Pvp;

namespace Maple2.Server.Tests.Game.Pvp;

// The individual arena queue (World) and the free-for-all PvP zone (field) on their own.
public class ArenaTests {
    // A full arena (playerCount) is matched at once, the longest waiting first.
    [Test]
    public void Queue_FullArenaAtOnce() {
        var queue = new ArenaQueue();
        for (long id = 1; id <= 3; id++) {
            Assert.That(queue.Register(id, 2, 3, id, out List<ArenaMatch> none), Is.EqualTo(RankDuelError.none));
            if (id < 3) {
                Assert.That(none, Is.Empty);
            } else {
                Assert.That(none.Single().Characters, Is.EqualTo(new long[] { 1, 2, 3 }));
                Assert.That(none.Single().Arena, Is.EqualTo(2));
            }
        }
        Assert.That(queue.MatchOf(2)!.Characters.Count, Is.EqualTo(3));
        Assert.That(queue.Register(2, 2, 3, 10, out _), Is.EqualTo(RankDuelError.Busy));
    }

    // Fewer than playerCount: at least two, once the longest has waited GatherMs. Arenas do not mix.
    [Test]
    public void Queue_GatherAfterTheWait() {
        var queue = new ArenaQueue();
        queue.Register(1, 2, 10, 0, out _);
        queue.Register(2, 2, 10, 1000, out _);
        queue.Register(3, 1, 10, 1000, out _);
        Assert.That(queue.Register(1, 2, 10, 1000, out _), Is.EqualTo(RankDuelError.Queued));
        Assert.That(queue.Pair(ArenaQueue.GatherMs - 1), Is.Empty);
        List<ArenaMatch> made = queue.Pair(ArenaQueue.GatherMs);
        Assert.That(made.Single().Characters, Is.EqualTo(new long[] { 1, 2 }));
        // Arena 1 has one waiter: no match.
        Assert.That(queue.Pair(ArenaQueue.GatherMs * 10), Is.Empty);
        Assert.That(queue.IsQueued(3), Is.True);
    }

    [Test]
    public void Queue_JoinEndExpire() {
        var queue = new ArenaQueue();
        queue.Register(1, 2, 2, 0, out _);
        queue.Register(2, 2, 2, 0, out List<ArenaMatch> made);
        ArenaMatch match = made.Single();
        Assert.That(queue.Join(1, out _), Is.EqualTo(RankDuelError.NoMatch));
        queue.SetRoom(match.Id, 1, 65000002, 55);
        Assert.That(queue.Join(1, out ArenaMatch? joined), Is.EqualTo(RankDuelError.none));
        Assert.That((joined!.MapId, joined.RoomId), Is.EqualTo((65000002, 55)));
        Assert.That(queue.End(match.Id), Is.SameAs(match));
        Assert.That(queue.MatchOf(1), Is.Null);

        queue.Register(3, 2, 2, 0, out _);
        queue.Register(4, 2, 2, 0, out made);
        queue.SetRoom(made.Single().Id, 1, 65000002, 56);
        Assert.That(queue.Expire(ArenaQueue.NoShowMs).Single().Id, Is.EqualTo(made.Single().Id));
        Assert.That(queue.Unregister(3), Is.EqualTo(RankDuelError.NotQueued));
    }

    private static PvpZoneState Zone(params long[] players) {
        var zone = new PvpZoneState(101, PvpZoneState.FfaMode, 0, 5, 300, 100, -50);
        Assert.That(zone.DueToActivate(4999), Is.False);
        Assert.That(zone.DueToActivate(5000), Is.True);
        zone.Activate(players);
        return zone;
    }

    // Each participant gets its own point from 101 up; only participants are each other's targets, while it runs.
    [Test]
    public void Zone_PointsAndHostility() {
        PvpZoneState zone = Zone(7, 8, 9);
        Assert.That(zone.Entries.Select(entry => entry.Point), Is.EqualTo(new[] { 101, 102, 103 }));
        Assert.That(zone.Hostile(7, 8), Is.True);
        Assert.That(zone.Hostile(7, 7), Is.False);
        Assert.That(zone.Hostile(7, 99), Is.False);
        Assert.That(zone.DueToEnd(5000 + 299_999), Is.False);
        Assert.That(zone.DueToEnd(5000 + 300_000), Is.True);
        zone.End();
        Assert.That(zone.Hostile(7, 8), Is.False);
    }

    // A death counts for the victim (-50) and, when it was hit, its last attacker (+100 and a kill).
    [Test]
    public void Zone_KillsDeathsDamage() {
        PvpZoneState zone = Zone(7, 8, 9);
        Assert.That(zone.Damage(7, 8, 40)!.Damage, Is.EqualTo(40));
        zone.Damage(9, 8, 10);
        List<PvpZoneEntry> changed = zone.Death(8, out long killer);
        Assert.That(killer, Is.EqualTo(9));
        Assert.That(changed.Select(entry => (entry.CharacterId, entry.Score, entry.Kill, entry.Death)),
            Is.EqualTo(new[] { (9L, 100, 1, 0), (8L, -50, 0, 1) }));
        // A death with no hit before it: no killer.
        changed = zone.Death(7, out killer);
        Assert.That(killer, Is.EqualTo(0));
        Assert.That(changed.Single().Score, Is.EqualTo(-50));
    }

    // The client's order: here first, score, kills, fewer deaths, damage, the higher id.
    [Test]
    public void Zone_Ranking() {
        PvpZoneState zone = Zone(1, 2, 3, 4);
        zone.Damage(1, 2, 5);
        zone.Death(2, out _);
        zone.Damage(3, 4, 50);
        zone.Damage(4, 3, 60);
        zone.SetHere(1, false);
        Assert.That(zone.Ranking().Select(entry => entry.CharacterId), Is.EqualTo(new long[] { 4, 3, 2, 1 }));
    }

    // The result dialog's reward: PvpFFAReward<rank>Count plus the rate per kill for ranks 1 to 10, the tenth count after.
    [Test]
    public void Zone_Reward() {
        int[] counts = [30, 25, 20, 15, 15, 15, 15, 10, 10, 10];
        Assert.That(PvpZoneState.Reward(1, 3, counts, 0), Is.EqualTo(30));
        Assert.That(PvpZoneState.Reward(2, 3, counts, 2), Is.EqualTo(31));
        Assert.That(PvpZoneState.Reward(10, 0, counts, 0), Is.EqualTo(10));
        Assert.That(PvpZoneState.Reward(15, 9, counts, 2), Is.EqualTo(10));
        Assert.That(PvpZoneState.Reward(1, 0, [], 0), Is.EqualTo(0));
    }
}

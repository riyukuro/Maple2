using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game.Pvp;

namespace Maple2.Server.Tests.Game.Pvp;

// The r4 arena rules on their own: the free-for-all's kill notices and kill credit, the arena queue's move and
// release and waits, the Red Arena's face-offs and Treasure Island's teams.
public class ArenaModesTests {
    private static PvpZoneState Ffa(params long[] players) {
        var zone = new PvpZoneState(101, PvpZoneState.FfaMode, 0, 0, 300, 100, -50) { ComboTickMs = 10_000, SlayerCount = 5, RulerCount = 10 };
        zone.Activate(players);
        return zone;
    }

    // 2 kills within the combo tick: a double kill; 3: a triple; the 5th and 10th kills of a streak: slayer, ruler.
    [Test]
    public void KillNotices() {
        PvpZoneState zone = Ffa(1, 2);
        var notices = new List<int>();
        for (int kill = 1; kill <= 10; kill++) {
            long tick = kill * 20_000L;
            if (kill is 2 or 3) {
                tick = 20_000L + (kill - 1) * 1_000L;
            }
            zone.Damage(1, 2, 10, tick);
            zone.Death(2, tick, out long killer, out int notice);
            Assert.That(killer, Is.EqualTo(1));
            notices.Add(notice);
        }
        Assert.That(notices, Is.EqualTo(new[] { 0, PvpKillNotice.DoubleKill, PvpKillNotice.TripleKill, 0, PvpKillNotice.Slayer, 0, 0, 0, 0, PvpKillNotice.Ruler }));
        // A death ends the streak.
        zone.Damage(2, 1, 10, 300_000);
        zone.Death(1, 300_000, out _, out _);
        Assert.That(zone.Entry(1)!.Streak, Is.EqualTo(0));
    }

    // A death long after the last hit is no kill.
    [Test]
    public void KillCredit_OnlyForARecentHit() {
        PvpZoneState zone = Ffa(1, 2);
        zone.Damage(1, 2, 10, 1_000);
        zone.Death(2, 1_000 + PvpZoneState.KillCreditMs + 1, out long killer, out _);
        Assert.That(killer, Is.EqualTo(0));
        Assert.That(zone.Entry(1)!.Kill, Is.EqualTo(0));
    }

    // Registering for another arena moves the waiter; the waits list each arena's first in line.
    [Test]
    public void Queue_MoveAndWaits() {
        var queue = new ArenaQueue();
        queue.Register(1, 1, 10, 0, out _);
        queue.Register(2, 3, 20, 500, out _);
        Assert.That(queue.WaitTimes(2_000), Is.EquivalentTo(new Dictionary<int, long> { [1] = 2_000, [3] = 1_500 }));
        Assert.That(queue.Register(1, 3, 20, 3_000, out _), Is.EqualTo(RankDuelError.none));
        Assert.That(queue.WaitTimes(4_000), Is.EquivalentTo(new Dictionary<int, long> { [3] = 3_500 }));
        Assert.That(queue.Register(1, 3, 20, 5_000, out _), Is.EqualTo(RankDuelError.Queued));
    }

    // Who did not join within the no-show time is let go; the match goes on for the others.
    [Test]
    public void Queue_ReleasesWhoDidNotJoin() {
        var queue = new ArenaQueue();
        queue.Register(1, 2, 3, 0, out _);
        queue.Register(2, 2, 3, 0, out _);
        queue.Register(3, 2, 3, 0, out List<ArenaMatch> made);
        ArenaMatch match = made.Single();
        queue.SetRoom(match.Id, 1, 65000002, 9);
        queue.Join(1, out _);
        queue.Join(2, out _);
        Assert.That(queue.Release(ArenaQueue.NoShowMs - 1), Is.Empty);
        Assert.That(queue.Release(ArenaQueue.NoShowMs), Is.EqualTo(new[] { (3L, 2) }));
        Assert.That(queue.Release(ArenaQueue.NoShowMs + 1), Is.Empty);
        Assert.That(queue.MatchOf(3), Is.Null);
        Assert.That(queue.MatchOf(1), Is.SameAs(match));
        Assert.That(queue.Register(3, 2, 3, 70_000, out _), Is.EqualTo(RankDuelError.none));
        queue.End(match.Id);
        Assert.That(queue.IsQueued(3), Is.True);
    }

    // Face-offs: two at random, then the winner against the next; a timeout leaves no winner; the zone ends when no
    // challenger is left, with the last winner as champion.
    [Test]
    public void RedArena_FaceOffs() {
        var state = new RedArenaState(102, 0, 1, 120, new Random(7));
        Assert.That(state.DueToActivate(999), Is.False);
        List<RedArenaEvent> events = state.Activate([1, 2, 3, 4], 1_000);
        RedArenaEvent prepare = events.Single();
        Assert.That(prepare.Type, Is.EqualTo(RedArenaEventType.Prepare));
        Assert.That(state.Hostile(prepare.First, prepare.Second), Is.False);
        Assert.That(state.Update(1_000 + RedArenaState.PrepareMs).Single().Type, Is.EqualTo(RedArenaEventType.Start));
        Assert.That(state.Hostile(prepare.First, prepare.Second), Is.True);
        long bystander = new long[] { 1, 2, 3, 4 }.First(id => id != prepare.First && id != prepare.Second);
        Assert.That(state.Hostile(prepare.First, bystander), Is.False);

        // The second fighter dies: the first wins (1 win) and meets a new challenger.
        events = state.Out(prepare.Second, 5_000);
        Assert.That(events[0], Is.EqualTo(new RedArenaEvent(RedArenaEventType.Result, prepare.First, prepare.Second, prepare.First, 1)));
        Assert.That(events[1].Type, Is.EqualTo(RedArenaEventType.Prepare));
        Assert.That(events[1].First, Is.EqualTo(prepare.First));
        long challenger = events[1].Second;

        // The time runs out: no winner, both out; the last waiting one and nobody else: the zone ends with no champion.
        state.Update(5_000 + RedArenaState.PrepareMs);
        events = state.Update(5_000 + RedArenaState.PrepareMs + RedArenaState.FightMs);
        Assert.That(events[0], Is.EqualTo(new RedArenaEvent(RedArenaEventType.Result, prepare.First, challenger, 0, 0)));
        Assert.That(events[1].Type, Is.EqualTo(RedArenaEventType.End));
        Assert.That(state.Champion, Is.EqualTo(0));
        Assert.That(state.Ended, Is.True);
    }

    [Test]
    public void RedArena_LastWinnerIsChampion() {
        var state = new RedArenaState(102, 0, 0, 120, new Random(1));
        RedArenaEvent prepare = state.Activate([1, 2], 0).Single();
        state.Update(RedArenaState.PrepareMs);
        List<RedArenaEvent> events = state.Out(prepare.First, 4_000);
        Assert.That(events.Last(), Is.EqualTo(new RedArenaEvent(RedArenaEventType.End, Winner: prepare.Second, Wins: 1)));
        Assert.That(state.Champion, Is.EqualTo(prepare.Second));
    }

    private static TeamZoneState Teams() {
        var state = new TeamZoneState(104, 0, 3, 600, [1, 2, 101, 102, 103]);
        Assert.That(state.DueToActivate(3_000), Is.True);
        state.Activate([10, 20, 30, 40], 3_000);
        return state;
    }

    // Dealt red, blue, red, blue; only the other team is hostile.
    [Test]
    public void Teams_Dealt() {
        TeamZoneState state = Teams();
        Assert.That(state.Entries.Select(entry => (entry.CharacterId, entry.Team)),
            Is.EqualTo(new[] { (10L, TeamZoneState.Red), (20L, TeamZoneState.Blue), (30L, TeamZoneState.Red), (40L, TeamZoneState.Blue) }));
        Assert.That(state.Hostile(10, 20), Is.True);
        Assert.That(state.Hostile(10, 30), Is.False);
        Assert.That((state.RedTreasureBox, state.BlueTreasureBox, string.Join(",", state.FlagBoxes)), Is.EqualTo((1, 2, "101,102,103")));
    }

    // A flag is taken after CaptureMs with only one team in its box and scores every ScoreTickMs; a plunder scores once
    // per cooldown; a kill scores; the higher score wins.
    [Test]
    public void Teams_FlagsPlunderKillsEnd() {
        TeamZoneState state = Teams();
        var boxes = new Dictionary<int, long[]> { [102] = [10], [2] = [30] };
        IEnumerable<long> InBox(int box) => boxes.GetValueOrDefault(box) ?? [];

        List<TeamZoneEvent> events = state.Update(4_000, InBox);
        Assert.That(events, Does.Contain(new TeamZoneEvent(TeamZoneEventType.Plunder, 30, TeamZoneState.Blue)));
        Assert.That(state.RedScore, Is.EqualTo(TeamZoneState.PlunderScore));
        // Contested: no capture.
        boxes[102] = [10, 20];
        state.Update(6_000, InBox);
        boxes[102] = [10];
        state.Update(7_000, InBox);
        Assert.That(state.Update(7_000 + TeamZoneState.CaptureMs - 1, InBox).Any(e => e.Type == TeamZoneEventType.Flag), Is.False);
        events = state.Update(7_000 + TeamZoneState.CaptureMs, InBox);
        Assert.That(events, Does.Contain(new TeamZoneEvent(TeamZoneEventType.Flag, Team: TeamZoneState.Red, Flag: 1)));
        Assert.That(state.FlagOwner(1), Is.EqualTo(TeamZoneState.Red));
        int before = state.RedScore;
        state.Update(7_000 + TeamZoneState.CaptureMs + TeamZoneState.ScoreTickMs, InBox);
        Assert.That(state.RedScore, Is.GreaterThan(before));

        state.Death(10, 20);
        Assert.That(state.BlueScore, Is.EqualTo(TeamZoneState.KillScore));
        Assert.That((state.Entry(10)!.Death, state.Entry(20)!.Kill), Is.EqualTo((1, 1)));
        Assert.That(state.RedGauge(), Is.EqualTo(state.RedScore * 1000 / (state.RedScore + state.BlueScore)));
        Assert.That(state.DueToEnd(603_000), Is.True);
        Assert.That(state.End(), Is.EqualTo(TeamZoneState.Red));
    }

    [Test]
    public void Teams_EqualIsADraw() {
        TeamZoneState state = Teams();
        Assert.That(state.RedGauge(), Is.EqualTo(500));
        Assert.That(state.End(), Is.EqualTo(0));
    }
}

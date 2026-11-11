using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game.Packets;

// The user's matchmaker rules (11-11) for Party 32/33/34, on the queue alone.
public class DungeonMatchQueueTests {
    private static readonly DungeonMatchRoom Four = new(100, 2, 4, false);
    private static readonly DungeonMatchRoom Three = new(200, 3, 3, false);
    private static readonly DungeonMatchRoom Change = new(300, 0, 4, true);

    [Test]
    public void MaxUsersAsGetMaxUserCount() {
        Assert.That(DungeonMatchRules.MaxUsers(Four, 1), Is.EqualTo(4));
        Assert.That(DungeonMatchRules.MaxUsers(Change, 1), Is.EqualTo(2));
        Assert.That(DungeonMatchRules.MaxUsers(Change, 3), Is.EqualTo(3));
        Assert.That(DungeonMatchRules.MaxUsers(Change, 9), Is.EqualTo(4));
    }

    [Test]
    public void FillsToTheMaximum() {
        var queue = new DungeonMatchQueue();
        Assert.That(queue.Start(1, 1, [Four], 1).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        DungeonMatchResult full = queue.Join(1, [Four])!.Value;
        Assert.That(full, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 100)));
        Assert.That(queue.IsSearching(1), Is.False);
        // The matched group is gone: the next searcher finds no group.
        Assert.That(queue.Join(1, [Four]), Is.Null);
    }

    [Test]
    public void PartyAtTheMaximumIsMatchedAtOnce() {
        var queue = new DungeonMatchQueue();
        Assert.That(queue.Start(1, 1, [Three], 3), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 200)));
        // Rooms the party is too large for are not searched.
        Assert.That(queue.Start(2, 1, [Three], 4).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
    }

    [Test]
    public void ChangeMaxUserRoomFullAtTwo() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Change], 1);
        Assert.That(queue.Join(1, [Change])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
    }

    [Test]
    public void JoinsTheLargestGroupWithSpace() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], 1);
        queue.Start(2, 1, [Four], 2);
        queue.Start(3, 1, [Four], 2);
        DungeonMatchResult joined = queue.Join(1, [Four])!.Value;
        // Two groups of 2: the one that started first.
        Assert.That(joined.PartyId, Is.EqualTo(2));
        Assert.That(queue.Join(1, [Four])!.Value, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 2, 1, 100)));
        Assert.That(queue.Join(1, [Four])!.Value.PartyId, Is.EqualTo(3));
    }

    [Test]
    public void JoinNeedsASharedRoomAndTheSameKind() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], 1);
        Assert.That(queue.Join(1, [Three]), Is.Null);
        Assert.That(queue.Join(2, [Four]), Is.Null);
    }

    [Test]
    public void JoinNarrowsTheRooms() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four, Three], 1);
        queue.Join(1, [Four]);
        // The group now searches room 100 only: a searcher for 200 alone does not fit.
        Assert.That(queue.Join(1, [Three]), Is.Null);
        Assert.That(queue.Join(1, [Four, Three])!.Value.PartyId, Is.EqualTo(1));
    }

    [Test]
    public void SeveralFullRoomsPickTheLowestId() {
        var queue = new DungeonMatchQueue();
        var other = new DungeonMatchRoom(50, 2, 3, false);
        queue.Start(1, 1, [Three, other], 2);
        Assert.That(queue.Join(1, [Three, other])!.Value.RoomId, Is.EqualTo(50));
    }

    [Test]
    public void LeaderStopsEarlyWithTheMinimum() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], 1);
        queue.Join(1, [Four]);
        Assert.That(queue.Cancel(1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 100)));
        Assert.That(queue.Cancel(1).Outcome, Is.EqualTo(DungeonMatchOutcome.NotSearching));
    }

    [Test]
    public void LeaderCancelUnderTheMinimumStops() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Three], 2);
        Assert.That(queue.Cancel(1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.IsSearching(1), Is.False);
    }

    [Test]
    public void NoMinimumStartsWithOneFound() {
        var queue = new DungeonMatchQueue();
        var solo = new DungeonMatchRoom(400, 0, 4, false);
        // Nobody found: a cancel is a plain cancel, even where one user may enter.
        queue.Start(1, 1, [solo], 1);
        Assert.That(queue.Cancel(1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        // A party of two at the minimum that found nobody stops too.
        queue.Start(2, 1, [Four], 2);
        Assert.That(queue.Cancel(2).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        queue.Start(3, 1, [solo], 1);
        queue.Join(1, [solo]);
        Assert.That(queue.Cancel(3), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 3, 1, 400)));
    }

    [Test]
    public void LeaveShrinksAndEmptyStops() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], 1);
        queue.Join(1, [Four]);
        Assert.That(queue.Leave(1).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Leave(1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.Leave(1).Outcome, Is.EqualTo(DungeonMatchOutcome.NotSearching));
    }

    [Test]
    public void ResizeFollowsThePartyCount() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four, Three], 2);
        // An invite brings the party to three: full for the three-user room.
        Assert.That(queue.Resize(1, 3), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 200)));
        Assert.That(queue.Resize(1, 3).Outcome, Is.EqualTo(DungeonMatchOutcome.NotSearching));

        queue.Start(2, 1, [Four, Three], 2);
        // Five members fit no room: the search stops.
        Assert.That(queue.Resize(2, 5).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.IsSearching(2), Is.False);

        queue.Start(3, 1, [Four], 3);
        Assert.That(queue.Resize(3, 2).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
    }

    [Test]
    public void StartTwiceIsAnotherMatch() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], 1);
        Assert.That(queue.Start(1, 1, [Four], 1).Outcome, Is.EqualTo(DungeonMatchOutcome.AlreadySearching));
        Assert.That(queue.KindOf(1), Is.EqualTo(1));
        Assert.That(queue.Remove(1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.KindOf(1), Is.EqualTo(0));
    }

    [Test]
    public void ConcurrentJoinsNeverOverfill() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], 1);
        var results = new System.Collections.Concurrent.ConcurrentBag<DungeonMatchResult?>();
        System.Threading.Tasks.Parallel.For(0, 20, _ => results.Add(queue.Join(1, [Four])));
        Assert.That(results.Count(result => result != null), Is.EqualTo(3));
        Assert.That(results.Count(result => result?.Outcome == DungeonMatchOutcome.Matched), Is.EqualTo(1));
    }

    private static DungeonRoomMetadata Room(int id, DungeonGroupType group, int gearScore, int minLevel) {
        var limit = new DungeonRoomLimitMetadata(0, 4, gearScore, minLevel, 0, false, [], [], [], false, false, false, false, false, 0);
        var reward = new DungeonRoomRewardMetadata(false, 0, 0, 0, 0, 0, [], [], 0, 0, 0);
        return new DungeonRoomMetadata(id, 0, DungeonPlayType.none, group, DungeonCooldownType.none, 0, 0, 0, [], reward, limit,
            0, 0, 0, false, 0, 0, false, [], [], false, default, false);
    }

    [Test]
    public void MatchRoomsAsGetMatchRooms() {
        DungeonRoomMetadata[] rooms = [
            Room(1, DungeonGroupType.normal, 0, 10),
            Room(2, DungeonGroupType.normal, 100, 20),
            Room(3, DungeonGroupType.raid, 200, 30),
            Room(4, DungeonGroupType.normal, 100, 50),
            Room(5, DungeonGroupType.lapenta, 0, 10),
        ];
        // From the highest at or under (gear score 150, level 40) down; room 4 needs level 50.
        List<int> ids = DungeonMatchRules.MatchRooms(rooms, 40, 150, 6).Select(room => room.Id).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 2, 1 }));
        // Room 3 is above the gear score; at level 60 room 4 is the highest.
        ids = DungeonMatchRules.MatchRooms(rooms, 60, 150, 6).Select(room => room.Id).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 4, 2, 1 }));
        // The pick count, and 0 for no limit.
        Assert.That(DungeonMatchRules.MatchRooms(rooms, 60, 500, 2).Select(room => room.Id), Is.EqualTo(new[] { 3, 4 }));
        Assert.That(DungeonMatchRules.MatchRooms(rooms, 60, 500, 0).Count, Is.EqualTo(4));
    }
}

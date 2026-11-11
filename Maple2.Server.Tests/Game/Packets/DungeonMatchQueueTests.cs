using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The user's matchmaker rules (11-11) for Party 32/33/34, on the queue alone.
public class DungeonMatchQueueTests {
    private static readonly DungeonMatchRoom Four = new(100, 2, 4, false);
    private static readonly DungeonMatchRoom Three = new(200, 3, 3, false);
    private static readonly DungeonMatchRoom Change = new(300, 0, 4, true);

    private long nextId = 1000;

    // n new character ids.
    private long[] M(int n) => Enumerable.Range(0, n).Select(_ => nextId++).ToArray();

    [Test]
    public void MaxUsersIsTheRowMaximum() {
        Assert.That(DungeonMatchRules.MaxUsers(Four), Is.EqualTo(4));
        Assert.That(DungeonMatchRules.MaxUsers(Change), Is.EqualTo(4));
    }

    [Test]
    public void FillsToTheMaximum() {
        var queue = new DungeonMatchQueue();
        Assert.That(queue.Start(1, 1, [Four], M(1)).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        DungeonMatchResult full = queue.Join(1, [Four], M(1))!.Value;
        Assert.That(full, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 100)));
        Assert.That(queue.IsSearching(1), Is.False);
        // The matched group is gone: the next searcher finds no group.
        Assert.That(queue.Join(1, [Four], M(1)), Is.Null);
    }

    [Test]
    public void PartyAtTheMaximumIsMatchedAtOnce() {
        var queue = new DungeonMatchQueue();
        Assert.That(queue.Start(1, 1, [Three], M(3)), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 200)));
        // Rooms the party is too large for are not searched.
        Assert.That(queue.Start(2, 1, [Three], M(4)).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
    }

    [Test]
    public void ChangeMaxUserRoomFillsToItsMaxUserCount() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Change], M(1));
        Assert.That(queue.Join(1, [Change], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Change], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Change], M(1))!.Value, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 300)));
        // A party of the row maximum is matched at once.
        Assert.That(queue.Start(2, 1, [Change], M(4)).Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
    }

    [Test]
    public void JoinsTheLargestGroupWithSpace() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], M(1));
        queue.Start(2, 1, [Four], M(2));
        queue.Start(3, 1, [Four], M(2));
        DungeonMatchResult joined = queue.Join(1, [Four], M(1))!.Value;
        // Two groups of 2: the one that started first.
        Assert.That(joined.PartyId, Is.EqualTo(2));
        Assert.That(queue.Join(1, [Four], M(1))!.Value, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 2, 1, 100)));
        Assert.That(queue.Join(1, [Four], M(1))!.Value.PartyId, Is.EqualTo(3));
    }

    [Test]
    public void JoinNeedsASharedRoomAndTheSameKind() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], M(1));
        Assert.That(queue.Join(1, [Three], M(1)), Is.Null);
        Assert.That(queue.Join(2, [Four], M(1)), Is.Null);
    }

    [Test]
    public void JoinNarrowsTheRooms() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four, Three], M(1));
        queue.Join(1, [Four], M(1));
        // The group now searches room 100 only: a searcher for 200 alone does not fit.
        Assert.That(queue.Join(1, [Three], M(1)), Is.Null);
        Assert.That(queue.Join(1, [Four, Three], M(1))!.Value.PartyId, Is.EqualTo(1));
    }

    [Test]
    public void SeveralFullRoomsPickTheLowestId() {
        var queue = new DungeonMatchQueue();
        var other = new DungeonMatchRoom(50, 2, 3, false);
        queue.Start(1, 1, [Three, other], M(2));
        Assert.That(queue.Join(1, [Three, other], M(1))!.Value.RoomId, Is.EqualTo(50));
    }

    [Test]
    public void LeaderStopsEarlyWithTheMinimum() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], M(1));
        queue.Join(1, [Four], M(1));
        Assert.That(queue.Cancel(1, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 100)));
        Assert.That(queue.Cancel(1, 1).Outcome, Is.EqualTo(DungeonMatchOutcome.NotSearching));
    }

    [Test]
    public void LeaderCancelUnderTheMinimumStops() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Three], M(2));
        Assert.That(queue.Cancel(1, 1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.IsSearching(1), Is.False);
    }

    [Test]
    public void NoMinimumStartsWithOneFound() {
        var queue = new DungeonMatchQueue();
        var solo = new DungeonMatchRoom(400, 0, 4, false);
        // Nobody found: a cancel is a plain cancel, even where one user may enter.
        queue.Start(1, 1, [solo], M(1));
        Assert.That(queue.Cancel(1, 1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        // A party of two at the minimum that found nobody stops too.
        queue.Start(2, 1, [Four], M(2));
        Assert.That(queue.Cancel(2, 1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        queue.Start(3, 1, [solo], M(1));
        queue.Join(1, [solo], M(1));
        Assert.That(queue.Cancel(3, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 3, 1, 400)));
    }

    [Test]
    public void LeaveShrinksAndEmptyStops() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1]);
        queue.Join(1, [Four], [2]);
        Assert.That(queue.Leave(1, [2]).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.CountOf(1), Is.EqualTo((1, 0)));
        Assert.That(queue.Leave(1, [1]).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.Leave(1, [1]).Outcome, Is.EqualTo(DungeonMatchOutcome.NotSearching));
    }

    [Test]
    public void ResizeFollowsThePartyCount() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four, Three], [1, 2]);
        // An invite brings the party to three: full for the three-user room.
        Assert.That(queue.Resize(1, [1, 2, 3]), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 200)));
        Assert.That(queue.Resize(1, [1, 2, 3]).Outcome, Is.EqualTo(DungeonMatchOutcome.NotSearching));

        queue.Start(2, 1, [Four, Three], [4, 5]);
        // Five members fit no room: the search stops.
        Assert.That(queue.Resize(2, [4, 5, 6, 7, 8]).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.IsSearching(2), Is.False);

        queue.Start(3, 1, [Four], [9, 10, 11]);
        Assert.That(queue.Resize(3, [9, 10]).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Four], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
    }

    // rv40 MINOR 8: a found member who leaves or goes offline is no longer found, so the leader's 34 is a plain
    // cancel again; the party's own members never count as found.
    [Test]
    public void ResizeLowersFound() {
        var queue = new DungeonMatchQueue();
        var five = new DungeonMatchRoom(500, 2, 5, false);
        queue.Start(1, 1, [five], [1, 2, 3]);
        queue.Join(1, [five], [9]);
        Assert.That(queue.CountOf(1), Is.EqualTo((4, 1)));
        // The stranger leaves: nobody is found, the cancel stops.
        queue.Resize(1, [1, 2, 3]);
        Assert.That(queue.CountOf(1), Is.EqualTo((3, 0)));
        Assert.That(queue.Cancel(1, 1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));

        // A friend going offline lowers the users but not the found stranger.
        queue.Start(2, 1, [five], [1, 2, 3]);
        queue.Join(1, [five], [9]);
        queue.Resize(2, [1, 2, 9]);
        Assert.That(queue.CountOf(2), Is.EqualTo((3, 1)));
        Assert.That(queue.Cancel(2, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 2, 1, 500)));
    }

    // rv40 MAJOR 2: only online members count; a group whose members all went offline stops.
    [Test]
    public void OfflineMembersDoNotFillTheGroup() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1]);
        // The solo searcher logs out.
        Assert.That(queue.Resize(1, []).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.Join(1, [Four], [2]), Is.Null);

        // A party of three with one offline is a group of two: a stranger makes three, not a full four.
        queue.Start(2, 1, [Four], [3, 4]);
        Assert.That(queue.Join(1, [Four], [5])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.CountOf(2), Is.EqualTo((3, 1)));
    }

    // rv40 MINOR 10: a cancel of another kind is not the party's search.
    [Test]
    public void CancelOfAnotherKindKeepsTheSearch() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1, 2]);
        Assert.That(queue.Cancel(1, 2).Outcome, Is.EqualTo(DungeonMatchOutcome.NotSearching));
        Assert.That(queue.IsSearching(1), Is.True);
        Assert.That(queue.Cancel(1, 1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
    }

    // The user's merge rule (11-11): a party that starts a search goes into a larger same-kind group sharing a
    // room, the largest then the earliest, unless the merged size is over that room's maxUserCount.
    [Test]
    public void PartyMergesIntoALargerGroup() {
        var queue = new DungeonMatchQueue();
        var eight = new DungeonMatchRoom(600, 2, 8, false);
        queue.Start(1, 1, [eight], [1, 2, 3]);
        queue.Start(2, 1, [eight], [4, 5, 6]);
        queue.Start(3, 1, [eight], [7]);
        DungeonMatchResult merged = queue.Join(1, [eight], [10, 11])!.Value;
        // Two groups of three, the earlier one; the group of one is smaller than the party.
        Assert.That(merged, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Searching, 1, 1)));
        Assert.That(queue.CountOf(1), Is.EqualTo((5, 2)));
        Assert.That(queue.CountOf(2), Is.EqualTo((3, 0)));
        // Found rose by the party's members: the leader may now start early.
        Assert.That(queue.Cancel(1, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 600)));
    }

    [Test]
    public void PartyDoesNotMergeIntoAGroupNotLarger() {
        var queue = new DungeonMatchQueue();
        var eight = new DungeonMatchRoom(600, 2, 8, false);
        queue.Start(1, 1, [eight], [1, 2]);
        Assert.That(queue.Join(1, [eight], [3, 4]), Is.Null);
        queue.Start(2, 1, [eight], [5]);
        Assert.That(queue.Join(1, [eight], [6, 7]), Is.Null);
        // Of another kind, or sharing no room: no merge either.
        queue.Start(3, 2, [eight], [8, 9, 10]);
        Assert.That(queue.Join(1, [eight], [11, 12]), Is.Null);
        queue.Start(4, 1, [Four], [13, 14, 15]);
        Assert.That(queue.Join(1, [eight], [16, 17]), Is.Null);
    }

    [Test]
    public void MergeOverTheCapIsRefused() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1, 2, 3]);
        // 3 + 2 is over room 100's 4: the party starts its own group.
        Assert.That(queue.Join(1, [Four], [4, 5]), Is.Null);
        Assert.That(queue.Start(2, 1, [Four], [4, 5]).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.CountOf(1), Is.EqualTo((3, 0)));

        // Of two shared rooms, only the one that holds the merged size stays.
        var eight = new DungeonMatchRoom(600, 2, 8, false);
        queue.Start(3, 1, [Four, eight], [6, 7, 8]);
        Assert.That(queue.Join(1, [Four, eight], [9, 10])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        // A solo for room 100 alone no longer fits group 3 (now at five, room 600 only).
        Assert.That(queue.Join(1, [Four], [11])!.Value.PartyId, Is.EqualTo(1));
    }

    [Test]
    public void MergeThatFillsTheCapMakesTheRoom() {
        var queue = new DungeonMatchQueue();
        var five = new DungeonMatchRoom(500, 2, 5, false);
        queue.Start(1, 1, [five], [1, 2, 3]);
        Assert.That(queue.Join(1, [five], [4, 5]), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 500)));
        Assert.That(queue.IsSearching(1), Is.False);
    }

    [Test]
    public void StartTwiceIsAnotherMatch() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], M(1));
        Assert.That(queue.Start(1, 1, [Four], M(1)).Outcome, Is.EqualTo(DungeonMatchOutcome.AlreadySearching));
        Assert.That(queue.KindOf(1), Is.EqualTo(1));
        Assert.That(queue.Remove(1).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
        Assert.That(queue.KindOf(1), Is.EqualTo(0));
    }

    [Test]
    public void ConcurrentJoinsNeverOverfill() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], M(1));
        var results = new System.Collections.Concurrent.ConcurrentBag<DungeonMatchResult?>();
        System.Threading.Tasks.Parallel.For(0, 20, _ => results.Add(queue.Join(1, [Four], M(1))));
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
    public void KindAsTheClientGroupTypeMap() {
        Assert.That(DungeonMatchRules.KindOf(DungeonGroupType.normal), Is.EqualTo(1));
        Assert.That(DungeonMatchRules.KindOf(DungeonGroupType.raid), Is.EqualTo(1));
        Assert.That(DungeonMatchRules.KindOf(DungeonGroupType.worldBossDungeon), Is.EqualTo(2));
        Assert.That(DungeonMatchRules.KindOf(DungeonGroupType.@event), Is.EqualTo(3));
        Assert.That(DungeonMatchRules.KindOf(DungeonGroupType.lapenta), Is.EqualTo(4));
        Assert.That(DungeonMatchRules.KindOf(DungeonGroupType.chaosRaid), Is.EqualTo(0));
        Assert.That(DungeonMatchRules.SearchBits(1), Is.EqualTo(2));
        Assert.That(DungeonMatchRules.SearchBits(4), Is.EqualTo(16));
        Assert.That(DungeonMatchRules.SearchBits(0), Is.EqualTo(0));
    }

    [Test]
    public void PickedRoomsKeepTheKindAndLevel() {
        var table = new Dictionary<int, DungeonRoomMetadata> {
            [1] = Room(1, DungeonGroupType.normal, 0, 10),
            [2] = Room(2, DungeonGroupType.normal, 0, 50),
            [3] = Room(3, DungeonGroupType.lapenta, 0, 10),
        };
        List<int> ids = DungeonMatchRules.PickedRooms(table, [1, 2, 3, 9, 1], 1, 40).Select(room => room.Id).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 1 }));
        Assert.That(DungeonMatchRules.PickedRooms(table, [3], 4, 40).Select(room => room.Id), Is.EqualTo(new[] { 3 }));
        Assert.That(DungeonMatchRules.PickedRooms(table, [3], 0, 40), Is.Empty);
    }

    // CPartyManager::OnPartyDungeonMatchRegister (0x1e): kind, registered, notice, then the bits IsMatchSearching tests.
    [Test]
    public void RegisterReplyCarriesTheSearchBit() {
        var reader = new ByteReader(PartyPacket.PartySearch(1, true).Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Party));
        Assert.That(reader.ReadByte(), Is.EqualTo(30));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));

        reader = new ByteReader(PartyPacket.PartySearch(1, false).Buffer, 0);
        reader.Read<SendOp>();
        reader.ReadByte();
        reader.ReadByte();
        Assert.That(reader.ReadBool(), Is.False);
        reader.ReadBool();
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
    }

    // rv40 MAJOR 3: the kind comes with the party World sends, so a Load during a search writes its bit.
    [Test]
    public void PartyInfoCarriesTheSearchKindIntoLoad() {
        var party = new Maple2.Model.Game.Party.Party(5, 1, 2, "a");
        Maple2.Server.Game.Manager.PartyManager.ApplyInfo(party, new Maple2.Server.World.Service.PartyInfo {
            DungeonId = 7, DungeonSet = true, DungeonRoomId = 9, DungeonMatchKind = 4,
        });
        Assert.That((party.DungeonMatchKind, party.DungeonId, party.DungeonSet, party.DungeonLobbyRoomId), Is.EqualTo(((byte) 4, 7, true, 9)));
        ByteWriter load = PartyPacket.Load(party);
        // The tail is the dungeon id, the searching bits, a 0 byte and no party search listing.
        Assert.That(load.Buffer[load.Length - 3], Is.EqualTo(16));

        Maple2.Server.Game.Manager.PartyManager.ApplyInfo(party, new Maple2.Server.World.Service.PartyInfo());
        load = PartyPacket.Load(party);
        Assert.That(load.Buffer[load.Length - 3], Is.EqualTo(0));
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

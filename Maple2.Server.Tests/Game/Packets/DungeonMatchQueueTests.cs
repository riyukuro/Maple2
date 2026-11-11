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
        Assert.That(full, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 100, 4)));
        Assert.That(queue.IsSearching(1), Is.False);
        // The matched group is gone: the next searcher finds no group.
        Assert.That(queue.Join(1, [Four], M(1)), Is.Null);
    }

    [Test]
    public void PartyAtTheMaximumIsMatchedAtOnce() {
        var queue = new DungeonMatchQueue();
        Assert.That(queue.Start(1, 1, [Three], M(3)), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 200, 3)));
        // Rooms the party is too large for are not searched.
        Assert.That(queue.Start(2, 1, [Three], M(4)).Outcome, Is.EqualTo(DungeonMatchOutcome.Stopped));
    }

    [Test]
    public void ChangeMaxUserRoomFillsToItsMaxUserCount() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Change], M(1));
        Assert.That(queue.Join(1, [Change], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Change], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.Join(1, [Change], M(1))!.Value, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 300, 4)));
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
        Assert.That(queue.Join(1, [Four], M(1))!.Value, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 2, 1, 100, 4)));
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
        Assert.That(queue.Cancel(1, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 100, 4)));
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
        Assert.That(queue.Cancel(3, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 3, 1, 400, 4)));
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
        Assert.That(queue.Resize(1, [1, 2, 3]), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 200, 3)));
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
        Assert.That(queue.Cancel(2, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 2, 1, 500, 5)));
    }

    // rv42 MAJOR 1: every member counts, online or not; an offline member keeps its slot.
    [Test]
    public void OfflineMembersKeepTheirSlot() {
        var queue = new DungeonMatchQueue();
        // A party of three, one of them offline, is a group of three: a stranger fills the four.
        queue.Start(2, 1, [Four], [3, 4, 6]);
        Assert.That(queue.Resize(2, [3, 4, 6]).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.CountOf(2), Is.EqualTo((3, 0)));
        Assert.That(queue.Join(1, [Four], [5])!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
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

    // The merge rule (rv42 MAJOR 2): a party that starts a search goes into a same-kind group of at least its size
    // sharing a room, the largest then the earliest, unless the merged size is over that room's maxUserCount.
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
        Assert.That(queue.Cancel(1, 1), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 600, 8)));
    }

    [Test]
    public void PartyDoesNotMergeIntoASmallerGroup() {
        var queue = new DungeonMatchQueue();
        var eight = new DungeonMatchRoom(600, 2, 8, false);
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
        Assert.That(queue.Join(1, [five], [4, 5]), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 500, 5)));
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

    [Test]
    public void APartyOfOneGoesWhenItsSearchEnds() {
        Assert.That(DungeonMatchRules.DisbandsWhenSearchEnds(1), Is.True);
        Assert.That(DungeonMatchRules.DisbandsWhenSearchEnds(0), Is.True);
        Assert.That(DungeonMatchRules.DisbandsWhenSearchEnds(2), Is.False);
    }

    [Test]
    public void ASearchingPartyIsKeptWhileAnotherMemberIsOnline() {
        // No search: the old rule, fewer than 2 online or at most 2 members.
        Assert.That(DungeonMatchRules.Disbands(2, 2, 1, false), Is.True);
        Assert.That(DungeonMatchRules.Disbands(4, 1, 0, false), Is.True);
        Assert.That(DungeonMatchRules.Disbands(3, 3, 2, false), Is.False);
        // Searching: a 2-member party keeps its search for the member left.
        Assert.That(DungeonMatchRules.Disbands(2, 2, 1, true), Is.False);
        Assert.That(DungeonMatchRules.Disbands(3, 2, 1, true), Is.False);
        // No one else online: it goes.
        Assert.That(DungeonMatchRules.Disbands(2, 1, 0, true), Is.True);
    }

    [Test]
    public void ALogoutKeepsTheSlotSoTheCapHolds() {
        var queue = new DungeonMatchQueue();
        var room = new DungeonMatchRoom(700, 1, 4, false);
        long[] members = M(3);
        Assert.That(queue.Start(1, 1, [room], members).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        // A member logs out: the group still counts three, so one solo fills the room and the one who comes back
        // does not make five.
        Assert.That(queue.CountOf(1).Users, Is.EqualTo(3));
        Assert.That(queue.Join(1, [room], M(1))!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(queue.IsSearching(1), Is.False);
    }

    [Test]
    public void SearchRoomsRecommendsOnlyForKindOne() {
        var table = new Dictionary<int, DungeonRoomMetadata> {
            [1] = Room(1, DungeonGroupType.normal, 0, 10),
            [5] = Room(5, DungeonGroupType.lapenta, 0, 10),
        };
        Assert.That(DungeonMatchRules.SearchRooms(table, [], 1, 40, 150, 6).Select(room => room.Id), Is.EqualTo(new[] { 1 }));
        // No picked room for a lapenta (4) search: no rooms, not the kind 1 recommendations.
        Assert.That(DungeonMatchRules.SearchRooms(table, [], 4, 40, 150, 6), Is.Empty);
        Assert.That(DungeonMatchRules.SearchRooms(table, [5], 4, 40, 150, 6).Select(room => room.Id), Is.EqualTo(new[] { 5 }));
    }

    // rv42 MAJOR 2: the smaller goes into the larger, on equal size the later into the earlier, at any start.
    private static DungeonMatchPull StartAndPull(DungeonMatchQueue queue, int partyId, DungeonMatchRoom room, long[] members) {
        Assert.That(queue.Join(1, [room], members), Is.Null);
        Assert.That(queue.Start(partyId, 1, [room], members).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        return queue.Pull(partyId);
    }

    [Test]
    public void ASoloThatWaitedGoesIntoALaterPartyOfThree() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1]);
        DungeonMatchPull pull = StartAndPull(queue, 2, Four, [2, 3, 4]);
        Assert.That(pull.Result, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 2, 1, 100, 4)));
        Assert.That(pull.Pulled.Select(group => group.PartyId), Is.EqualTo(new[] { 1 }));
        Assert.That(queue.IsSearching(1), Is.False);
    }

    [Test]
    public void TwoAndTwoOnACapOfFourMatch() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1, 2]);
        Assert.That(queue.Join(1, [Four], [3, 4]), Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 1, 1, 100, 4)));
    }

    [Test]
    public void TwoThenThreeOnACapOfFiveMatch() {
        var queue = new DungeonMatchQueue();
        var five = new DungeonMatchRoom(500, 2, 5, false);
        queue.Start(1, 1, [five], [1, 2]);
        DungeonMatchPull pull = StartAndPull(queue, 2, five, [3, 4, 5]);
        Assert.That(pull.Result, Is.EqualTo(new DungeonMatchResult(DungeonMatchOutcome.Matched, 2, 1, 500, 5)));
        Assert.That(pull.Before!.Users, Is.EqualTo(3));
    }

    [Test]
    public void ThreeAndThreeOnACapOfFourStayApart() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1, 2, 3]);
        DungeonMatchPull pull = StartAndPull(queue, 2, Four, [4, 5, 6]);
        Assert.That(pull.Result.Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(pull.Pulled, Is.Empty);
        Assert.That(queue.CountOf(1).Users, Is.EqualTo(3));
        Assert.That(queue.CountOf(2).Users, Is.EqualTo(3));
    }

    [Test]
    public void APullTakesTheLargestThenTheEarliestWhileTheyFit() {
        var queue = new DungeonMatchQueue();
        var six = new DungeonMatchRoom(600, 2, 6, false);
        queue.Start(1, 1, [six], [1]);
        queue.Start(2, 1, [six], [2, 3]);
        queue.Start(3, 1, [six], [4]);
        DungeonMatchPull pull = StartAndPull(queue, 4, six, [5, 6, 7]);
        // 3 + 2 + 1 = 6: the two, then the solo that started first; the later solo waits.
        Assert.That(pull.Pulled.Select(group => group.PartyId), Is.EqualTo(new[] { 2, 1 }));
        Assert.That(pull.Result.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(queue.IsSearching(3), Is.True);
    }

    [Test]
    public void AMergeNeverPassesThePartyCapacity() {
        var queue = new DungeonMatchQueue(_ => 3);
        var eight = new DungeonMatchRoom(600, 2, 8, false);
        queue.Start(1, 1, [eight], [1, 2]);
        Assert.That(queue.Join(1, [eight], [3, 4]), Is.Null);
        Assert.That(queue.Join(1, [eight], [5])!.Value.PartyId, Is.EqualTo(1));
    }

    [Test]
    public void APartyThatMayNotMergeIsSkipped() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1]);
        queue.Start(2, 1, [Four], [2, 3, 4]);
        Assert.That(queue.Pull(2, partyId => partyId != 1).Pulled, Is.Empty);
        // A solo goes to the smaller group 1, as group 2 may not take it.
        Assert.That(queue.Join(1, [Four], [5], out _, partyId => partyId != 2)!.Value.PartyId, Is.EqualTo(1));
        Assert.That(queue.Join(1, [Four], [6], out _, partyId => false), Is.Null);
    }

    // rv42 MINOR 5: a join or pull whose party side failed is put back, not matched.
    [Test]
    public void RestorePutsTheGroupsBack() {
        var queue = new DungeonMatchQueue();
        queue.Start(1, 1, [Four], [1, 2, 3]);
        Assert.That(queue.Join(1, [Four], [4], out DungeonMatchGroup? before)!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(queue.Restore(before!).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(queue.CountOf(1), Is.EqualTo((3, 0)));

        var six = new DungeonMatchRoom(600, 2, 6, false);
        queue.Start(2, 1, [six], [5]);
        queue.Start(3, 1, [six], [6, 7]);
        DungeonMatchPull pull = StartAndPull(queue, 4, six, [8, 9, 10]);
        Assert.That(pull.Result.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        // The move of party 2 failed, party 3's went through: 4 searches with five, 2 searches again.
        DungeonMatchGroup failed = pull.Pulled.Single(group => group.PartyId == 2);
        Assert.That(queue.Restore(pull.Before!, pull.Pulled.Where(group => group != failed)).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        queue.Restore(failed);
        Assert.That(queue.CountOf(4), Is.EqualTo((5, 2)));
        Assert.That(queue.CountOf(2), Is.EqualTo((1, 0)));
    }

    // rv42 MINOR 6: a matched party stays marked until its room is set or failed; a new search is refused then.
    [Test]
    public void AMatchedPartyCannotSearchUntilItsRoomIsSet() {
        var queue = new DungeonMatchQueue();
        Assert.That(queue.Start(1, 1, [Four], M(4)).Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(queue.IsMatching(1), Is.True);
        Assert.That(queue.IsSearching(1), Is.False);
        Assert.That(queue.Start(1, 1, [Four], M(2)).Outcome, Is.EqualTo(DungeonMatchOutcome.AlreadySearching));
        queue.EndMatching(1);
        Assert.That(queue.Start(1, 1, [Four], M(2)).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));

        // An early start is a match too; a group put back is no longer matching.
        queue.Join(1, [Four], M(1));
        Assert.That(queue.Cancel(1, 1).Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(queue.IsMatching(1), Is.True);
        queue.EndMatching(1);
        queue.Start(2, 1, [Four], M(3));
        Assert.That(queue.Join(1, [Four], M(1), out DungeonMatchGroup? before)!.Value.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(queue.IsMatching(2), Is.True);
        queue.Restore(before!);
        Assert.That(queue.IsMatching(2), Is.False);
    }
}

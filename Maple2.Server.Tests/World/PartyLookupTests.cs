using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.World;

// The World side of the dungeon matchmaker (r60): PartyLookup with no channel, its room calls recorded.
public class PartyLookupTests {
    private sealed class TestPartyLookup : PartyLookup {
        public int RoomId = 9000;
        public Action? WhileMakingRoom;
        public bool Throw;
        public bool Async;
        public readonly HashSet<int> FailMoveFrom = [];
        public bool FailSoloJoin;
        public readonly List<int> Made = [];
        public readonly List<FieldRequest> Requests = [];
        public readonly List<int> Destroyed = [];
        public DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public readonly List<(DungeonRecruit Recruit, List<long> Targets)> Notices = [];

        public TestPartyLookup(PartySearchLookup partySearch) : base(new ChannelClientLookup(), null!, partySearch, null!) {
            DungeonRecruits = new DungeonRecruitBoard(() => Now);
        }

        protected override void SendDungeonRecruit(PartyManager manager, DungeonRecruit recruit, List<PlayerInfo> targets) =>
            Notices.Add((recruit, targets.Select(target => target.CharacterId).ToList()));

        protected override int CreateMatchedRoom(FieldRequest request) {
            Made.Add(request.CreateDungeon.PartyId);
            Requests.Add(request);
            WhileMakingRoom?.Invoke();
            if (Throw) {
                throw new InvalidOperationException("room");
            }
            return RoomId;
        }

        protected override void DestroyMatchedRoom(int lobbyRoomId) => Destroyed.Add(lobbyRoomId);

        public readonly List<(long Leader, string Joiner)> JoinRequests = [];
        protected override void SendJoinRequest(PartyManager manager, PlayerInfo leader, string joinerName) =>
            JoinRequests.Add((leader.CharacterId, joinerName));

        public override PartyError MoveMembers(PartyManager from, PartyManager to) =>
            FailMoveFrom.Contains(from.Party.Id) ? PartyError.s_party_err_full_limit_player : base.MoveMembers(from, to);

        protected override PartyError JoinSolo(PartyManager target, PlayerInfo info) =>
            FailSoloJoin ? PartyError.s_party_err_full_limit_player : base.JoinSolo(target, info);

        public readonly List<Action> VoteTimers = [];
        protected internal override void AfterVoteTime(PartyManager manager, Action expire) => VoteTimers.Add(expire);

        protected override void RunRoomTask(Action work) {
            if (Async) {
                base.RunRoomTask(work);
            } else {
                work();
            }
        }
    }

    private long nextId = 5000;
    private PartySearchLookup partySearch = null!;
    private TestPartyLookup lookup = null!;

    [SetUp]
    public void SetUp() {
        partySearch = new PartySearchLookup(new ChannelClientLookup());
        lookup = new TestPartyLookup(partySearch);
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
        partySearch.Dispose();
    }

    private PlayerInfo Player() {
        long id = nextId++;
        return new PlayerInfo(new CharacterInfo(id, id, "p" + id, "", "", Gender.Male, Job.Newbie, 60), "", new AchievementInfo(), []) {
            Channel = 1,
        };
    }

    // A party of this many online members.
    private PartyManager Party(int members) {
        PlayerInfo leader = Player();
        Assert.That(lookup.Create(leader.CharacterId, out int partyId, leader), Is.EqualTo(PartyError.none));
        Assert.That(lookup.TryGet(partyId, out PartyManager? manager), Is.True);
        for (int i = 1; i < members; i++) {
            Assert.That(manager!.Join(Player()), Is.EqualTo(PartyError.none));
        }
        return manager!;
    }

    private DungeonMatchResult Start(PartyManager manager, DungeonMatchRoom room) =>
        lookup.DungeonMatch.Start(manager.Party.Id, 1, [room], manager.MemberIds());

    [Test]
    public void AMatchedRoomIsSetOnTheParty() {
        PartyManager party = Party(2);
        DungeonMatchResult result = Start(party, new DungeonMatchRoom(100, 1, 2, false));
        Assert.That(result.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(lookup.DungeonMatch.IsMatching(party.Party.Id), Is.True);

        lookup.CompleteDungeonMatch(party, result);
        Assert.That(lookup.Made, Is.EqualTo(new[] { party.Party.Id }));
        Assert.That(lookup.Destroyed, Is.Empty);
        Assert.That(party.Party.DungeonSet, Is.True);
        Assert.That(party.Party.DungeonLobbyRoomId, Is.EqualTo(9000));
        Assert.That(lookup.DungeonMatch.IsMatching(party.Party.Id), Is.False);
    }

    // rv42 MINOR 6: the party went while its room was being made: the room is destroyed.
    [Test]
    public void ARoomForAPartyGoneMeanwhileIsDestroyed() {
        PartyManager party = Party(2);
        int partyId = party.Party.Id;
        DungeonMatchResult result = Start(party, new DungeonMatchRoom(100, 1, 2, false));
        lookup.WhileMakingRoom = () => lookup.Disband(partyId);

        lookup.CompleteDungeonMatch(party, result);
        Assert.That(lookup.TryGet(partyId, out _), Is.False);
        Assert.That(lookup.Destroyed, Is.EqualTo(new[] { 9000 }));
        Assert.That(lookup.DungeonMatch.IsMatching(partyId), Is.False);
    }

    [Test]
    public void ARoomThatCouldNotBeMadeIsNotDestroyed() {
        PartyManager party = Party(2);
        DungeonMatchResult result = Start(party, new DungeonMatchRoom(100, 1, 2, false));
        lookup.RoomId = 0;
        lookup.CompleteDungeonMatch(party, result);
        Assert.That(lookup.Destroyed, Is.Empty);
        Assert.That(party.Party.DungeonSet, Is.False);
        Assert.That(lookup.DungeonMatch.IsMatching(party.Party.Id), Is.False);
    }

    // rv44 MINOR 2: a room call that throws still ends the matching mark; the party may search again.
    [Test]
    public void ARoomCallThatThrowsEndsTheMatchingMark() {
        PartyManager party = Party(2);
        var room = new DungeonMatchRoom(100, 1, 2, false);
        DungeonMatchResult result = Start(party, room);
        lookup.Throw = true;
        lookup.CompleteDungeonMatch(party, result);
        Assert.That(lookup.DungeonMatch.IsMatching(party.Party.Id), Is.False);
        Assert.That(party.Party.DungeonSet, Is.False);
        Assert.That(Start(party, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
    }

    // rv44 MINOR 1: the room call runs off the effect drain; a later step's effect runs while it waits.
    [Test]
    public void ASlowRoomCallHoldsNoOtherEffect() {
        PartyManager party = Party(2);
        DungeonMatchResult result = Start(party, new DungeonMatchRoom(100, 1, 2, false));
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        lookup.Async = true;
        lookup.WhileMakingRoom = () => {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
        };
        lookup.CompleteDungeonMatch(party, result);
        Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);

        bool ran = false;
        lookup.DungeonMatchStep(() => PartyLookup.Defer(() => ran = true));
        Assert.That(ran, Is.True);
        Assert.That(lookup.DungeonMatch.IsMatching(party.Party.Id), Is.True);

        gate.Set();
        Assert.That(SpinWait.SpinUntil(() => party.Party.DungeonSet, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(lookup.DungeonMatch.IsMatching(party.Party.Id), Is.False);
    }

    // rv44 MINOR 5: a group a leave leaves searching joins a group at least its size that now holds it.
    [Test]
    public void AGroupShrunkByALeaveJoinsAnother() {
        var room = new DungeonMatchRoom(100, 1, 4, false);
        PartyManager waiting = Party(2);
        Assert.That(Start(waiting, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        PartyManager shrinking = Party(3);
        Assert.That(Start(shrinking, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(lookup.PullDungeonMatch(shrinking).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        int shrinkingId = shrinking.Party.Id;

        long leaver = shrinking.MemberIds().First(id => id != shrinking.Party.LeaderCharacterId);
        Assert.That(shrinking.Leave(leaver), Is.EqualTo(PartyError.none));
        Assert.That(lookup.TryGet(shrinkingId, out _), Is.False);
        Assert.That(waiting.Party.Members.Count, Is.EqualTo(4));
        Assert.That(lookup.Made, Is.EqualTo(new[] { waiting.Party.Id }));
        Assert.That(waiting.Party.DungeonSet, Is.True);
    }

    // rv44 MINOR 5: a group a leave leaves searching pulls the smaller groups that now fit.
    [Test]
    public void AGroupShrunkByALeavePullsASmallerOne() {
        var room = new DungeonMatchRoom(100, 1, 5, false);
        PartyManager waiting = Party(2);
        Assert.That(Start(waiting, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        PartyManager shrinking = Party(4);
        Assert.That(Start(shrinking, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(lookup.PullDungeonMatch(shrinking).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        int waitingId = waiting.Party.Id;

        long leaver = shrinking.MemberIds().First(id => id != shrinking.Party.LeaderCharacterId);
        Assert.That(shrinking.Leave(leaver), Is.EqualTo(PartyError.none));
        Assert.That(lookup.TryGet(waitingId, out _), Is.False);
        Assert.That(shrinking.Party.Members.Count, Is.EqualTo(5));
        Assert.That(lookup.Made, Is.EqualTo(new[] { shrinking.Party.Id }));
    }

    private PartyResponse Search(PlayerInfo requestor, DungeonMatchRoom room) =>
        lookup.SearchDungeonMatch(requestor.CharacterId, requestor, 1, [room]);

    private PlayerInfo Leader(PartyManager manager) => manager.Party.Members[manager.Party.LeaderCharacterId].Info;

    // dungeon_random_bonus: every room the matchmaker makes is a random match room (the client's whole matchmaker is
    // the random match), whichever search brought its members.
    private void AssertMadeRandom(params int[] dungeonIds) {
        Assert.That(lookup.Requests.Select(request => (request.CreateDungeon.DungeonId, request.CreateDungeon.RandomMatch)),
            Is.EqualTo(dungeonIds.Select(id => (id, true))));
    }

    // Party 32 with no room picked: the recommended rooms.
    [Test]
    public void ARecommendedRoomsSearchMakesARandomMatchRoom() {
        DungeonMatchRoom[] rooms = [new DungeonMatchRoom(100, 1, 2, false), new DungeonMatchRoom(200, 1, 2, false)];
        PlayerInfo first = Player();
        PlayerInfo second = Player();
        Assert.That(lookup.SearchDungeonMatch(first.CharacterId, first, 1, [..rooms]).Error, Is.EqualTo(0));
        Assert.That(lookup.Made, Is.Empty);
        Assert.That(lookup.SearchDungeonMatch(second.CharacterId, second, 1, [rooms[1]]).Error, Is.EqualTo(0));
        AssertMadeRandom(200);
    }

    // Party 33: the room dialog's party search, one room, reaches World as a search for that room alone.
    [Test]
    public void APartySearchForOneRoomMakesARandomMatchRoom() {
        var room = new DungeonMatchRoom(100, 1, 2, false);
        Assert.That(Search(Player(), room).Error, Is.EqualTo(0));
        Assert.That(Search(Player(), room).Error, Is.EqualTo(0));
        AssertMadeRandom(100);
    }

    // Party 34: the party goes with the members found so far; the room is still the matchmaker's.
    [Test]
    public void ACancelMadeRoomIsARandomMatchRoom() {
        var room = new DungeonMatchRoom(100, 1, 4, false);
        PartyResponse first = Search(Player(), room);
        Assert.That(first.Error, Is.EqualTo(0));
        Assert.That(Search(Player(), room).Error, Is.EqualTo(0));
        Assert.That(lookup.Made, Is.Empty);
        Assert.That(lookup.TryGet(first.PartyId, out PartyManager? party), Is.True);

        DungeonMatchResult result = lookup.DungeonMatch.Cancel(first.PartyId, 1);
        Assert.That(result.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        lookup.CompleteDungeonMatch(party!, result);
        AssertMadeRandom(100);
        Assert.That(party!.Party.DungeonSet, Is.True);
    }

    // rv44 MINOR 6: a party whose move into the group it joined fails puts the group back and searches on its own.
    [Test]
    public void AFailedPartyMoveStartsTheSearchersOwnSearch() {
        var room = new DungeonMatchRoom(100, 1, 4, false);
        PartyManager target = Party(2);
        Assert.That(Start(target, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        PartyManager searcher = Party(2);
        lookup.FailMoveFrom.Add(searcher.Party.Id);

        PartyResponse response = Search(Leader(searcher), room);
        Assert.That(response.Error, Is.EqualTo(0));
        Assert.That(response.PartyId, Is.EqualTo(searcher.Party.Id));
        Assert.That(lookup.DungeonMatch.IsSearching(searcher.Party.Id), Is.True);
        Assert.That(lookup.DungeonMatch.IsSearching(target.Party.Id), Is.True);
        Assert.That(lookup.DungeonMatch.CountOf(target.Party.Id).Users, Is.EqualTo(2));
        Assert.That(target.Party.Members.Count, Is.EqualTo(2));
        Assert.That(lookup.Made, Is.Empty);
    }

    // rv44 MINOR 6: a solo searcher whose join fails gets a party of their own that searches.
    [Test]
    public void AFailedSoloJoinStartsTheSearchersOwnSearch() {
        var room = new DungeonMatchRoom(100, 1, 4, false);
        PartyManager target = Party(2);
        Assert.That(Start(target, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        lookup.FailSoloJoin = true;
        PlayerInfo solo = Player();

        PartyResponse response = Search(solo, room);
        Assert.That(response.Error, Is.EqualTo(0));
        Assert.That(lookup.TryGetByCharacter(solo.CharacterId, out PartyManager? own), Is.True);
        Assert.That(response.PartyId, Is.EqualTo(own!.Party.Id));
        Assert.That(response.PartyId, Is.Not.EqualTo(target.Party.Id));
        Assert.That(lookup.DungeonMatch.IsSearching(own.Party.Id), Is.True);
        Assert.That(lookup.DungeonMatch.CountOf(target.Party.Id).Users, Is.EqualTo(2));
        Assert.That(target.Party.Members.Count, Is.EqualTo(2));
    }

    // rv44 MINOR 6: a pulled party whose move fails searches on its own again; the puller keeps searching.
    [Test]
    public void AFailedPulledMoveSearchesOnItsOwn() {
        var room = new DungeonMatchRoom(100, 1, 4, false);
        PartyManager waiting = Party(1);
        Assert.That(Start(waiting, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        lookup.FailMoveFrom.Add(waiting.Party.Id);
        PartyManager puller = Party(3);

        PartyResponse response = Search(Leader(puller), room);
        Assert.That(response.PartyId, Is.EqualTo(puller.Party.Id));
        Assert.That(lookup.DungeonMatch.IsSearching(waiting.Party.Id), Is.True);
        Assert.That(lookup.DungeonMatch.CountOf(waiting.Party.Id).Users, Is.EqualTo(1));
        Assert.That(lookup.DungeonMatch.IsSearching(puller.Party.Id), Is.True);
        Assert.That(lookup.DungeonMatch.CountOf(puller.Party.Id).Users, Is.EqualTo(3));
        Assert.That(lookup.DungeonMatch.IsMatching(puller.Party.Id), Is.False);
        Assert.That(lookup.Made, Is.Empty);

        // Without the failure the same pull fills the room.
        lookup.FailMoveFrom.Clear();
        Assert.That(lookup.PullDungeonMatch(puller).Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
    }

    // rv44 MINOR 3: no join while the party is matched, nor past the set room's maxUserCount.
    [Test]
    public void AMatchedPartyTakesNoJoinUntilItsRoomIsSetWithSpace() {
        PartyManager party = Party(2);
        DungeonMatchResult result = Start(party, new DungeonMatchRoom(100, 1, 2, false));
        Assert.That(result.MaxUsers, Is.EqualTo(2));
        Assert.That(party.Join(Player()), Is.EqualTo(PartyError.s_party_err_full_limit_player));
        // The matchmaker's own join of a searcher it counted is not refused.
        Assert.That(party.Join(Player(), matchmaker: true), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Members.Count, Is.EqualTo(3));
    }

    [Test]
    public void ASetRoomTakesJoinsUpToItsMaxUserCount() {
        PartyManager party = Party(2);
        DungeonMatchResult result = Start(party, new DungeonMatchRoom(100, 1, 2, false));
        lookup.CompleteDungeonMatch(party, result);
        Assert.That(party.Party.DungeonSet, Is.True);
        Assert.That(party.Join(Player()), Is.EqualTo(PartyError.s_party_err_full_limit_player));

        // A room set by the leader (manual): joins up to its cap, then none; unset, the party's capacity again.
        PartyManager manual = Party(2);
        manual.SetDungeon(manual.Party.LeaderCharacterId, 100, true, 9001, maxUsers: 3);
        Assert.That(manual.Join(Player()), Is.EqualTo(PartyError.none));
        Assert.That(manual.Join(Player()), Is.EqualTo(PartyError.s_party_err_full_limit_player));
        manual.SetDungeon(manual.Party.LeaderCharacterId, 0, false);
        Assert.That(manual.Join(Player()), Is.EqualTo(PartyError.none));
    }

    // rv44 MINOR 4: the leader's own room is refused while the party searches or is matched.
    [Test]
    public void ALeadersRoomIsRefusedWhileSearchingOrMatched() {
        PartyManager party = Party(2);
        long leader = party.Party.LeaderCharacterId;
        Assert.That(Start(party, new DungeonMatchRoom(100, 1, 4, false)).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(party.SetDungeon(leader, 200, true, 9001, maxUsers: 4), Is.EqualTo(PartyError.s_party_err_dungeon_match_another));
        Assert.That(party.Party.DungeonSet, Is.False);
        // Unsetting is not refused.
        Assert.That(party.SetDungeon(leader, 0, false), Is.EqualTo(PartyError.none));

        PartyManager matched = Party(2);
        DungeonMatchResult result = Start(matched, new DungeonMatchRoom(100, 1, 2, false));
        Assert.That(matched.SetDungeon(matched.Party.LeaderCharacterId, 200, true, 9001), Is.EqualTo(PartyError.s_party_err_dungeon_match_another));
        lookup.CompleteDungeonMatch(matched, result);
        Assert.That(matched.Party.DungeonLobbyRoomId, Is.EqualTo(9000));
        // After the match the leader may set a room again.
        Assert.That(matched.SetDungeon(matched.Party.LeaderCharacterId, 200, true, 9001), Is.EqualTo(PartyError.none));
    }

    // rv42 MINOR 9: the Party Finder listing goes with a disbanded party and with a party that moves away.
    [Test]
    public void ADisbandRemovesThePartysListing() {
        PartyManager party = Party(3);
        partySearch.Create(party.Party, "a", false, 4, out long listingId);
        Assert.That(lookup.Disband(party.Party.Id), Is.EqualTo(PartyError.none));
        Assert.That(partySearch.TryGet(listingId, out _), Is.False);
        Assert.That(party.Party.Search, Is.Null);
    }

    [Test]
    public void AMoveRemovesTheMovedPartysListingOnly() {
        PartyManager from = Party(2);
        PartyManager to = Party(2);
        partySearch.Create(from.Party, "a", false, 4, out long fromListing);
        partySearch.Create(to.Party, "b", false, 4, out long toListing);
        Assert.That(lookup.MoveMembers(from, to), Is.EqualTo(PartyError.none));
        Assert.That(partySearch.TryGet(fromListing, out _), Is.False);
        Assert.That(partySearch.TryGet(toListing, out _), Is.True);
        Assert.That(to.Party.Search!.MemberCount, Is.EqualTo(4));
    }

    // Dispose ends every party's listing, not only the last party's.
    [Test]
    public void DisposeEndsEveryPartysListing() {
        PartyManager first = Party(2);
        PartyManager second = Party(2);
        partySearch.Create(first.Party, "a", false, 4, out _);
        partySearch.Create(second.Party, "b", false, 4, out _);
        lookup.Dispose();
        Assert.That(first.Party.Search, Is.Null);
        Assert.That(second.Party.Search, Is.Null);
    }

    // rv42 MAJOR 2: the pulled parties really move into the new group's party, and the cap holds.
    [Test]
    public void APullMovesThePartiesIn() {
        var room = new DungeonMatchRoom(100, 1, 6, false);
        PartyManager solo = Party(1);
        PartyManager two = Party(2);
        PartyManager late = Party(1);
        Start(solo, room);
        Start(two, room);
        Start(late, room);
        PartyManager three = Party(3);
        Assert.That(Start(three, room).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));

        DungeonMatchResult result = lookup.PullDungeonMatch(three);
        Assert.That(result.Outcome, Is.EqualTo(DungeonMatchOutcome.Matched));
        Assert.That(three.Party.Members.Count, Is.EqualTo(6));
        Assert.That(three.Party.Members.Keys, Is.SupersetOf(solo.MemberIds().Concat(two.MemberIds())));
        Assert.That(lookup.TryGet(solo.Party.Id, out _), Is.False);
        Assert.That(lookup.TryGet(two.Party.Id, out _), Is.False);
        // The later solo did not fit: it keeps its party and its search.
        Assert.That(lookup.TryGet(late.Party.Id, out _), Is.True);
        Assert.That(lookup.DungeonMatch.IsSearching(late.Party.Id), Is.True);
    }

    [Test]
    public void APullStopsAtTheRoomCapAndThePartyCapacity() {
        var four = new DungeonMatchRoom(100, 1, 4, false);
        PartyManager pair = Party(2);
        Start(pair, four);
        PartyManager three = Party(3);
        Start(three, four);
        Assert.That(lookup.PullDungeonMatch(three).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(three.Party.Members.Count, Is.EqualTo(3));
        Assert.That(pair.Party.Members.Count, Is.EqualTo(2));
        Assert.That(lookup.DungeonMatch.IsSearching(pair.Party.Id), Is.True);

        // The room holds ten, the party only four (its capacity): the pair waits.
        var ten = new DungeonMatchRoom(200, 1, 10, false);
        PartyManager otherPair = Party(2);
        Start(otherPair, ten);
        PartyManager small = Party(3);
        small.Party.Capacity = 4;
        Start(small, ten);
        Assert.That(lookup.PullDungeonMatch(small).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(small.Party.Members.Count, Is.EqualTo(3));
        Assert.That(lookup.TryGet(otherPair.Party.Id, out _), Is.True);
    }

    // A party with a dungeon room set is not pulled (rv42 MINOR 8).
    [Test]
    public void APartyWithADungeonSetIsNotPulled() {
        var room = new DungeonMatchRoom(100, 1, 4, false);
        PartyManager solo = Party(1);
        Start(solo, room);
        solo.Party.DungeonSet = true;
        PartyManager three = Party(3);
        Start(three, room);
        Assert.That(lookup.PullDungeonMatch(three).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        Assert.That(lookup.TryGet(solo.Party.Id, out _), Is.True);
    }

    // RoomDungeon 15/16 and Party 39/43 (r63).
    private static readonly DungeonRecruitRoom RecruitRoom = new(200, 50, 0, 4);

    private PartyResponse Recruit(PlayerInfo requestor, IReadOnlyCollection<PlayerInfo> online, DungeonRecruitRoom? room = null) =>
        lookup.StartDungeonRecruit(requestor, DungeonRecruitKind.Member, room ?? RecruitRoom, online);

    [Test]
    public void TheNoticeGoesToMembersAndToOnlinePlayersWhoCouldJoin() {
        PartyManager party = Party(2);
        PlayerInfo free = Player();
        PlayerInfo low = Player();
        low.Level = 49;
        PlayerInfo offline = Player();
        offline.Channel = -1;
        PartyManager full = Party(4);
        full.Party.Capacity = 4;
        PartyManager matching = Party(2);
        lookup.DungeonMatch.Start(matching.Party.Id, 1, [new DungeonMatchRoom(100, 1, 4, false)], matching.MemberIds());
        Assert.That(lookup.PullDungeonMatch(matching).Outcome, Is.EqualTo(DungeonMatchOutcome.Searching));
        PartyManager open = Party(2);
        PlayerInfo[] online = [Leader(party), free, low, offline, Leader(full), Leader(matching), Leader(open)];

        PartyResponse response = Recruit(Leader(party), online);

        Assert.That(response.Error, Is.EqualTo(0));
        Assert.That(response.PartyId, Is.EqualTo(party.Party.Id));
        Assert.That(lookup.Notices, Has.Count.EqualTo(1));
        Assert.That(lookup.Notices[0].Targets, Is.EquivalentTo(party.MemberIds().Append(free.CharacterId).Append(Leader(open).CharacterId)));
        Assert.That(lookup.Notices[0].Recruit.Kind, Is.EqualTo(DungeonRecruitKind.Member));
        Assert.That(DungeonRecruitRules.NeedMembers(RecruitRoom, party.Party.Members.Count), Is.EqualTo(2));
    }

    [Test]
    public void APlayerWithNoPartyRecruitsInAPartyOfOne() {
        PlayerInfo solo = Player();
        PartyResponse response = Recruit(solo, [solo]);
        Assert.That(lookup.TryGetByCharacter(solo.CharacterId, out PartyManager? made), Is.True);
        Assert.That(response.PartyId, Is.EqualTo(made!.Party.Id));
        Assert.That(made.Party.LeaderCharacterId, Is.EqualTo(solo.CharacterId));
        Assert.That(lookup.Notices[0].Targets, Is.EqualTo(new[] { solo.CharacterId }));
    }

    [Test]
    public void OnlyTheLeaderRecruits() {
        PartyManager party = Party(2);
        PlayerInfo member = party.Party.Members.Values.First(m => m.CharacterId != party.Party.LeaderCharacterId).Info;
        Assert.That(Recruit(member, []).Error, Is.EqualTo((int) PartyError.s_party_err_not_chief));
        Assert.That(lookup.Notices, Is.Empty);
    }

    [Test]
    public void ASecondNoticeInsideTheCooldownGetsTheTimeLeft() {
        PartyManager party = Party(1);
        Recruit(Leader(party), []);
        lookup.Now += TimeSpan.FromSeconds(4);

        PartyResponse again = Recruit(Leader(party), []);

        Assert.That(again.Error, Is.EqualTo(0));
        Assert.That(again.CooldownMilliseconds, Is.EqualTo(6000));
        Assert.That(lookup.Notices, Has.Count.EqualTo(1));

        lookup.Now += TimeSpan.FromSeconds(6);
        PartyResponse after = Recruit(Leader(party), []);
        Assert.That(after.CooldownMilliseconds, Is.EqualTo(0));
        Assert.That(lookup.Notices, Has.Count.EqualTo(2));
    }

    [Test]
    public void ASearchingPartyDoesNotRecruit() {
        PartyManager party = Party(1);
        lookup.DungeonMatch.Start(party.Party.Id, 1, [new DungeonMatchRoom(100, 1, 4, false)], party.MemberIds());
        Assert.That(Recruit(Leader(party), []).Error, Is.EqualTo((int) PartyError.s_party_err_dungeon_match_another));
    }

    [Test]
    public void AFullPartyDoesNotRecruit() {
        PartyManager party = Party(4);
        Assert.That(Recruit(Leader(party), []).Error, Is.EqualTo((int) PartyError.s_party_err_full_limit_player));
    }

    [Test]
    public void AJoinThroughTheLinkAddsAnOrdinaryMember() {
        PartyManager party = Party(1);
        Recruit(Leader(party), []);
        PlayerInfo joiner = Player();
        Assert.That(lookup.JoinDungeonRecruit(joiner, party.Party.Id, RecruitRoom.Id), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Members.ContainsKey(joiner.CharacterId), Is.True);
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Not.Null);
    }

    [Test]
    public void AJoinerInAPartyIsRefused() {
        PartyManager party = Party(1);
        Recruit(Leader(party), []);
        PartyManager other = Party(1);
        Assert.That(lookup.JoinDungeonRecruit(Leader(other), party.Party.Id, RecruitRoom.Id), Is.EqualTo(PartyError.s_party_err_already));
        Assert.That(party.Party.Members, Has.Count.EqualTo(1));
    }

    // The join that fills the room closes the recruit, so a later one is refused as closed (31); JoinDungeonRecruit's
    // own cap branch (19) is not reached through World, only the rule is.
    [Test]
    public void AFullPartyClosesItsRecruitAndRefuses() {
        PartyManager party = Party(3);
        Recruit(Leader(party), [], new DungeonRecruitRoom(200, 50, 0, 4));
        Assert.That(lookup.JoinDungeonRecruit(Player(), party.Party.Id, 200), Is.EqualTo(PartyError.none));
        // Full: the recruit closed; a closed recruit refuses.
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Null);
        Assert.That(lookup.JoinDungeonRecruit(Player(), party.Party.Id, 200), Is.EqualTo(PartyError.s_party_err_not_found));
        Assert.That(DungeonRecruitRules.RefusesJoin(Player(), new DungeonRecruitRoom(200, 50, 0, 4), 4), Is.EqualTo(PartyError.s_party_err_full_limit_player));
        Assert.That(party.Party.Members, Has.Count.EqualTo(4));
    }

    [Test]
    public void AJoinerBelowTheRoomsLimitsIsRefused() {
        PartyManager party = Party(1);
        Leader(party).GearScore = 1000; // the requester is held to the room too
        Assert.That(Recruit(Leader(party), [], new DungeonRecruitRoom(200, 50, 1000, 4)).Error, Is.EqualTo(0));
        PlayerInfo weak = Player();
        weak.GearScore = 999;
        Assert.That(lookup.JoinDungeonRecruit(weak, party.Party.Id, 200), Is.EqualTo(PartyError.s_party_err_lack_gear_score));
        PlayerInfo low = Player();
        low.Level = 49;
        Assert.That(lookup.JoinDungeonRecruit(low, party.Party.Id, 200), Is.EqualTo(PartyError.s_party_err_lack_level));
    }

    [Test]
    public void AJoinToAClosedOrUnknownRecruitIsRefused() {
        PartyManager party = Party(1);
        Assert.That(lookup.JoinDungeonRecruit(Player(), party.Party.Id, RecruitRoom.Id), Is.EqualTo(PartyError.s_party_err_not_found));
        Recruit(Leader(party), []);
        Assert.That(lookup.JoinDungeonRecruit(Player(), party.Party.Id, RecruitRoom.Id + 1), Is.EqualTo(PartyError.s_party_err_not_found));
        Assert.That(lookup.JoinDungeonRecruit(Player(), 999999, RecruitRoom.Id), Is.EqualTo(PartyError.s_party_err_not_found));
    }

    [Test]
    public void TheRecruitClosesAfterSixtySeconds() {
        PartyManager party = Party(1);
        Recruit(Leader(party), []);
        lookup.Now += TimeSpan.FromSeconds(59);
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Not.Null);
        lookup.Now += TimeSpan.FromSeconds(1);
        Assert.That(lookup.JoinDungeonRecruit(Player(), party.Party.Id, RecruitRoom.Id), Is.EqualTo(PartyError.s_party_err_not_found));
    }

    [Test]
    public void TheRecruitClosesWhenTheRoomIsUnsetOrChanged() {
        PartyManager party = Party(1);
        long leaderId = party.Party.LeaderCharacterId;
        Recruit(Leader(party), []);
        Assert.That(party.SetDungeon(leaderId, RecruitRoom.Id, true), Is.EqualTo(PartyError.none));
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Not.Null);
        Assert.That(party.SetDungeon(leaderId, RecruitRoom.Id, false), Is.EqualTo(PartyError.none));
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Null);

        lookup.Now += TimeSpan.FromSeconds(10);
        Recruit(Leader(party), []);
        Assert.That(party.SetDungeon(leaderId, RecruitRoom.Id + 1, true), Is.EqualTo(PartyError.none));
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Null);
    }

    [Test]
    public void TheRecruitClosesWhenThePartySearches() {
        PartyManager party = Party(1);
        Recruit(Leader(party), []);
        PartyResponse search = lookup.SearchDungeonMatch(party.Party.LeaderCharacterId, Leader(party), 1, [new DungeonMatchRoom(100, 1, 4, false)]);
        Assert.That(search.Error, Is.EqualTo(0));
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Null);
    }

    [Test]
    public void TheRecruitClosesWhenThePartyFillsUp() {
        PartyManager party = Party(3);
        party.Party.Capacity = 4;
        Recruit(Leader(party), [], new DungeonRecruitRoom(200, 50, 0, 10));
        Assert.That(party.Join(Player()), Is.EqualTo(PartyError.none));
        Assert.That(lookup.DungeonRecruits.Get(party.Party.Id), Is.Null);
    }
    // rv46 MAJOR 1: the wait goes with the requester, not with the party, so leaving and recruiting again waits.
    [Test]
    public void ARecruitAfterLeavingThePartyStillWaits() {
        PlayerInfo solo = Player();
        PartyResponse first = Recruit(solo, [solo]);
        Assert.That(lookup.Disband(first.PartyId), Is.EqualTo(PartyError.none));
        lookup.Now += TimeSpan.FromSeconds(4);
        PartyResponse again = Recruit(solo, [solo]);
        Assert.That(again.CooldownMilliseconds, Is.EqualTo(6000));
        Assert.That(again.PartyId, Is.EqualTo(0));
        Assert.That(lookup.TryGetByCharacter(solo.CharacterId, out _), Is.False);
        Assert.That(lookup.Notices, Has.Count.EqualTo(1));

        lookup.Now += TimeSpan.FromSeconds(6);
        Assert.That(Recruit(solo, [solo]).CooldownMilliseconds, Is.EqualTo(0));
        Assert.That(lookup.Notices, Has.Count.EqualTo(2));
    }

    // A leader who recruited in one party waits in the next one too, the larger time left counting.
    [Test]
    public void ALeaderWhoDisbandsAndLeadsAnotherPartyWaits() {
        PartyManager party = Party(1);
        PlayerInfo leader = Leader(party);
        Recruit(leader, []);
        Assert.That(lookup.Disband(party.Party.Id), Is.EqualTo(PartyError.none));
        lookup.Now += TimeSpan.FromSeconds(3);
        Assert.That(lookup.Create(leader.CharacterId, out int nextId, leader), Is.EqualTo(PartyError.none));
        Assert.That(lookup.TryGet(nextId, out PartyManager? next), Is.True);
        Assert.That(next!.Join(Player()), Is.EqualTo(PartyError.none));
        Assert.That(Recruit(leader, []).CooldownMilliseconds, Is.EqualTo(7000));
        Assert.That(lookup.Notices, Has.Count.EqualTo(1));
        Assert.That(next.Party.Members, Has.Count.EqualTo(2));
    }

    // Waits past the cooldown are dropped when a notice starts.
    [Test]
    public void OldWaitsAreDropped() {
        Recruit(Player(), []);
        Recruit(Player(), []);
        Assert.That(lookup.DungeonRecruits.RequestersKept, Is.EqualTo(2));
        lookup.Now += TimeSpan.FromSeconds(10);
        Recruit(Player(), []);
        Assert.That(lookup.DungeonRecruits.RequestersKept, Is.EqualTo(1));
    }

    // rv46 MINOR 2: a party with another room set does not recruit for this one (43); for its own room it does.
    [Test]
    public void APartyWithAnotherRoomSetDoesNotRecruit() {
        PartyManager party = Party(1);
        long leaderId = party.Party.LeaderCharacterId;
        Assert.That(party.SetDungeon(leaderId, RecruitRoom.Id + 1, true), Is.EqualTo(PartyError.none));
        Assert.That(Recruit(Leader(party), []).Error, Is.EqualTo((int) PartyError.s_party_err_dungeon_match_another));
        Assert.That(lookup.Notices, Is.Empty);
        Assert.That(party.SetDungeon(leaderId, RecruitRoom.Id, true), Is.EqualTo(PartyError.none));
        Assert.That(Recruit(Leader(party), []).Error, Is.EqualTo(0));
        Assert.That(lookup.Notices, Has.Count.EqualTo(1));
    }

    // rv46 MINOR 4: the requester is held to the room's level and gear score (17, 18), with no party of one made.
    [Test]
    public void ARequesterBelowTheRoomsLimitsDoesNotRecruit() {
        PlayerInfo low = Player();
        low.Level = 10;
        Assert.That(Recruit(low, [low]).Error, Is.EqualTo((int) PartyError.s_party_err_lack_level));
        Assert.That(lookup.TryGetByCharacter(low.CharacterId, out _), Is.False);
        PlayerInfo weak = Player();
        Assert.That(Recruit(weak, [weak], new DungeonRecruitRoom(200, 50, 1000, 4)).Error, Is.EqualTo((int) PartyError.s_party_err_lack_gear_score));
        Assert.That(lookup.Notices, Is.Empty);
    }

    // Client: PartyVoteCli::vfunc_1 keeps the vote's number and PartyVoteCli::SendParty answers with it. A ready
    // check run again after a "not ready" keeps its full time: the first check's timer does not end it.
    [Test]
    public void OldReadyCheckTimerDoesNotEndANewerCheck() {
        PartyManager party = Party(2);
        long leader = party.Party.LeaderCharacterId;
        long member = party.Party.Members.Keys.First(id => id != leader);

        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
        int first = party.Party.Vote!.Id;
        Assert.That(party.ReadyCheckReply(member, first, false), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote, Is.Null);

        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
        int second = party.Party.Vote!.Id;
        Assert.That(second, Is.GreaterThan(first));

        lookup.VoteTimers[0]();
        Assert.That(party.Party.Vote, Is.Not.Null);
        Assert.That(party.Party.Vote!.Id, Is.EqualTo(second));

        lookup.VoteTimers[1]();
        Assert.That(party.Party.Vote, Is.Null);
    }

    // An answer carrying an earlier vote's number is not counted in the running vote.
    [Test]
    public void AnswerToAnEarlierVoteIsNotCounted() {
        PartyManager party = Party(3);
        long leader = party.Party.LeaderCharacterId;
        long member = party.Party.Members.Keys.First(id => id != leader);

        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
        int id = party.Party.Vote!.Id;
        Assert.That(party.ReadyCheckReply(member, id + 1, true), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote!.Approvals, Does.Not.Contain(member));
        Assert.That(party.ReadyCheckReply(member, id, true), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote!.Approvals, Does.Contain(member));
    }

    // One vote at a time (client error s_party_err_already_vote): a second ready check or a kick vote is refused.
    [Test]
    public void SecondVoteIsRefusedWhileOneRuns() {
        PartyManager party = Party(4);
        long leader = party.Party.LeaderCharacterId;
        long target = party.Party.Members.Keys.First(id => id != leader);

        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
        int id = party.Party.Vote!.Id;
        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.s_party_err_already_vote));
        Assert.That(party.VoteKick(leader, target), Is.EqualTo(PartyError.s_party_err_already_vote));
        Assert.That(party.Party.Vote!.Id, Is.EqualTo(id));
        Assert.That(lookup.VoteTimers, Has.Count.EqualTo(1));
    }

    // A kick vote started by a member who is not the leader removes the target once it passes, and ends: a later
    // answer kicks no one and a new vote can start.
    [Test]
    public void PassedKickVoteRemovesTheTargetAndEnds() {
        PartyManager party = Party(4);
        long leader = party.Party.LeaderCharacterId;
        long[] others = party.Party.Members.Keys.Where(id => id != leader).ToArray();
        long starter = others[0], target = others[1], voter = others[2];

        Assert.That(party.VoteKick(starter, target), Is.EqualTo(PartyError.none));
        int id = party.Party.Vote!.Id;
        Assert.That(party.ReadyCheckReply(voter, id, true), Is.EqualTo(PartyError.none));

        Assert.That(party.Party.Members.ContainsKey(target), Is.False);
        Assert.That(party.Party.Vote, Is.Null);
        Assert.That(party.ReadyCheckReply(leader, id, true), Is.EqualTo(PartyError.s_party_err_not_found));
        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote!.Id, Is.GreaterThan(id));

        // The kick vote's timer, run late, leaves the new ready check alone.
        lookup.VoteTimers[0]();
        Assert.That(party.Party.Vote, Is.Not.Null);
    }

    // A member who leaves during a ready check is taken off it: once everyone left has answered, the check ends
    // instead of running out its time.
    [Test]
    public void ReadyCheckEndsWhenTheLastUnansweredMemberLeaves() {
        PartyManager party = Party(3);
        long leader = party.Party.LeaderCharacterId;
        long[] others = party.Party.Members.Keys.Where(id => id != leader).ToArray();

        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
        int id = party.Party.Vote!.Id;
        Assert.That(party.ReadyCheckReply(others[0], id, true), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote, Is.Not.Null);

        Assert.That(party.Leave(others[1]), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote, Is.Null);
        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
    }

    // The same for a member the leader kicks; an answer the member gave is taken off with them.
    [Test]
    public void KickedMemberIsTakenOffTheReadyCheck() {
        PartyManager party = Party(4);
        long leader = party.Party.LeaderCharacterId;
        long[] others = party.Party.Members.Keys.Where(id => id != leader).ToArray();

        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
        int id = party.Party.Vote!.Id;
        Assert.That(party.ReadyCheckReply(others[0], id, false), Is.EqualTo(PartyError.none));
        Assert.That(party.Kick(leader, others[0]), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote!.Voters, Does.Not.Contain(others[0]));
        Assert.That(party.Party.Vote!.Disapprovals, Does.Not.Contain(others[0]));

        Assert.That(party.ReadyCheckReply(others[1], id, true), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote, Is.Not.Null);
        Assert.That(party.Kick(leader, others[2]), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote, Is.Null);
    }

    // A kick vote whose target leaves ends, so the party can vote again.
    [Test]
    public void KickVoteEndsWhenItsTargetLeaves() {
        PartyManager party = Party(4);
        long leader = party.Party.LeaderCharacterId;
        long[] others = party.Party.Members.Keys.Where(id => id != leader).ToArray();

        Assert.That(party.VoteKick(others[0], others[1]), Is.EqualTo(PartyError.none));
        Assert.That(party.Leave(others[2]), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote, Is.Not.Null);
        Assert.That(party.Leave(others[1]), Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Vote, Is.Null);
        Assert.That(party.StartReadyCheck(leader), Is.EqualTo(PartyError.none));
    }

    // Party 0x17, the party finder's Join (partymanager.cpp:774-796).
    private static PartySearch List(PartyManager manager, int size, bool noApproval) {
        manager.Party.Search = new PartySearch(77, "listing", size) {
            PartyId = manager.Party.Id,
            NoApproval = noApproval,
            LeaderName = manager.Party.LeaderName,
        };
        return manager.Party.Search;
    }

    // With approval: the leader gets 0x2c with the joiner's name, the joiner 0x20 with the leader's; nobody joins yet.
    [Test]
    public void AJoinToAListingWithApprovalAsksTheLeader() {
        PartyManager party = Party(2);
        PartySearch search = List(party, 4, noApproval: false);
        PlayerInfo joiner = Player();

        (PartyError error, string leaderName) = lookup.JoinPartySearch(joiner, party.Party.Id, search.Id);
        Assert.Multiple(() => {
            Assert.That(error, Is.EqualTo(PartyError.s_party_request_invite));
            Assert.That(leaderName, Is.EqualTo(Leader(party).Name));
            Assert.That(lookup.JoinRequests, Is.EqualTo(new[] { (Leader(party).CharacterId, joiner.Name) }));
            Assert.That(party.Party.Members, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AJoinToAListingWithoutApprovalJoins() {
        PartyManager party = Party(1);
        PartySearch search = List(party, 4, noApproval: true);
        PlayerInfo joiner = Player();

        Assert.That(lookup.JoinPartySearch(joiner, party.Party.Id, search.Id).Error, Is.EqualTo(PartyError.none));
        Assert.That(party.Party.Members.ContainsKey(joiner.CharacterId), Is.True);
        Assert.That(lookup.JoinRequests, Is.Empty);
    }

    // The listing gone, changed or full meanwhile: its own error, and the leader is not asked.
    [Test]
    public void AJoinToAListingGoneOrFullMeanwhileIsRefused() {
        PartyManager party = Party(2);
        PartySearch search = List(party, 3, noApproval: false);

        Assert.Multiple(() => {
            Assert.That(lookup.JoinPartySearch(Player(), 999999, search.Id).Error, Is.EqualTo(PartyError.s_party_err_not_found));
            Assert.That(lookup.JoinPartySearch(Player(), party.Party.Id, search.Id + 1).Error, Is.EqualTo(PartyError.s_party_err_wrong_recruit));
        });
        Assert.That(party.Join(Player()), Is.EqualTo(PartyError.none));
        Assert.That(lookup.JoinPartySearch(Player(), party.Party.Id, search.Id).Error, Is.EqualTo(PartyError.s_party_err_full_limit_player));
        party.Party.Search = null;
        Assert.That(lookup.JoinPartySearch(Player(), party.Party.Id, search.Id).Error, Is.EqualTo(PartyError.s_party_err_invalid_recruit));

        // The leader went offline: the listing is out of date.
        PartyManager other = Party(2);
        PartySearch otherSearch = List(other, 4, noApproval: false);
        Leader(other).Channel = -1;
        Assert.That(lookup.JoinPartySearch(Player(), other.Party.Id, otherSearch.Id).Error, Is.EqualTo(PartyError.s_party_err_wrong_recruit));
        Assert.That(lookup.JoinRequests, Is.Empty);
    }

    // A player in a party is refused; the client shows s_party_err_already.
    [Test]
    public void AJoinerInAPartyIsRefusedByTheListing() {
        PartyManager party = Party(1);
        PartySearch search = List(party, 4, noApproval: false);
        PartyManager other = Party(1);
        Assert.That(lookup.JoinPartySearch(Leader(other), party.Party.Id, search.Id).Error, Is.EqualTo(PartyError.s_party_err_already));
        Assert.That(lookup.JoinRequests, Is.Empty);
    }

    // The joiner's answer (OnPartyResultMessage, partymanager.cpp:1370): result byte 0x20 and the leader's name;
    // the leader's line (partymanager.cpp:1434): 0x2c and the joiner's name.
    [Test]
    public void TheJoinAnswersAreTheClientsPackets() {
        string Hex(string text) => System.Convert.ToHexString(System.BitConverter.GetBytes((short) text.Length)
            .Concat(System.Text.Encoding.Unicode.GetBytes(text)).ToArray());
        string op = System.Convert.ToHexString(System.BitConverter.GetBytes((short) Maple2.Server.Core.Constants.SendOp.Party));
        Assert.Multiple(() => {
            Assert.That(System.Convert.ToHexString(Maple2.Server.Game.Packets.PartyPacket.Error(PartyError.s_party_request_invite, "Lead").ToArray()),
                Is.EqualTo(op + "00" + "20" + Hex("Lead")));
            Assert.That(System.Convert.ToHexString(Maple2.Server.Game.Packets.PartyPacket.JoinRequest("Asker").ToArray()),
                Is.EqualTo(op + "2C" + Hex("Asker")));
        });
    }
}

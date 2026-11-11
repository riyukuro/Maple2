using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The World side of the dungeon matchmaker (r60): PartyLookup with no channel, its room calls recorded.
public class PartyLookupTests {
    private sealed class TestPartyLookup : PartyLookup {
        public int RoomId = 9000;
        public Action? WhileMakingRoom;
        public bool Throw;
        public readonly List<int> Made = [];
        public readonly List<int> Destroyed = [];

        public TestPartyLookup(PartySearchLookup partySearch) : base(new ChannelClientLookup(), null!, partySearch, null!) { }

        protected override int CreateMatchedRoom(long leaderId, int partyId, int dungeonId, int size) {
            Made.Add(partyId);
            WhileMakingRoom?.Invoke();
            if (Throw) {
                throw new InvalidOperationException("room");
            }
            return RoomId;
        }

        protected override void DestroyMatchedRoom(int lobbyRoomId) => Destroyed.Add(lobbyRoomId);
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
}

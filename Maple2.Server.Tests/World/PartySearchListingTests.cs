using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.World;

// The party finder listing on the World side: one per party, and it goes with the party a channel loads.
public class PartySearchListingTests {
    private PartySearchLookup lookup = null!;

    [SetUp]
    public void SetUp() {
        lookup = new PartySearchLookup(new ChannelClientLookup());
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
    }

    // The client's cancel (PartySearch 1) names no listing, so a second listing for the same party would stay in
    // everyone's list after the cancel removed the other one.
    [Test]
    public void ASecondListingForAPartyIsRefused() {
        var party = new Party(5, 10, 20, "leader");

        Assert.That(lookup.Create(party, "first", false, 10, out long first), Is.EqualTo(PartySearchError.none));
        Assert.That(lookup.Create(party, "second", false, 10, out long second),
            Is.EqualTo(PartySearchError.s_partysearch_err_server_already_register));

        Assert.That(second, Is.EqualTo(0));
        Assert.That(party.Search!.Id, Is.EqualTo(first));
        Assert.That(lookup.TryGetByPartyId(5, out PartySearchManager? listing), Is.True);
        Assert.That(listing!.PartySearch.Name, Is.EqualTo("first"));

        Assert.That(lookup.Remove(first), Is.EqualTo(PartySearchError.none));
        Assert.That(lookup.TryGetByPartyId(5, out _), Is.False, "the cancel leaves no listing behind");
        Assert.That(party.Search, Is.Null);
    }

    [Test]
    public void APartyListsAgainAfterItsCancel() {
        var party = new Party(5, 10, 20, "leader");
        Assert.That(lookup.Create(party, "first", false, 10, out long first), Is.EqualTo(PartySearchError.none));
        Assert.That(lookup.Remove(first), Is.EqualTo(PartySearchError.none));

        Assert.That(lookup.Create(party, "again", true, 4, out long again), Is.EqualTo(PartySearchError.none));
        Assert.That(again, Is.Not.EqualTo(first));
    }

    // A member's session made again (map change through migration, channel change, relog) loads the party from
    // World's PartyInfo; the listing must be in it, or the owner's client forgets it.
    [Test]
    public void ThePartyInfoCarriesTheListing() {
        var party = new Party(5, 10, 20, "leader");
        party.Members.TryAdd(20, new PartyMember {
            PartyId = 5,
            Info = new PlayerInfo(new CharacterInfo(10, 20, "leader", "", "", Gender.Male, Job.Newbie, 60), "", new AchievementInfo(), []),
            JoinTime = 1,
        });
        Assert.That(lookup.Create(party, "come", false, 10, out long listingId), Is.EqualTo(PartySearchError.none));

        PartyInfo info = WorldService.PartyInfoOf(party, 0);

        Assert.That(info.PartySearch, Is.Not.Null);
        Assert.That(info.PartySearch.Id, Is.EqualTo(listingId));
        Assert.That(info.PartySearch.Name, Is.EqualTo("come"));
        Assert.That(info.PartySearch.Size, Is.EqualTo(10));
        Assert.That(info.PartySearch.MemberCount, Is.EqualTo(1));
        Assert.That(info.PartySearch.LeaderCharacterId, Is.EqualTo(20));

        Assert.That(lookup.Remove(listingId), Is.EqualTo(PartySearchError.none));
        Assert.That(WorldService.PartyInfoOf(party, 0).PartySearch, Is.Null);
    }
}

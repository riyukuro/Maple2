using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Error;
using Maple2.Model.Game.Party;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using PartySearchSort = Maple2.Model.Enum.PartySearchSort;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The party finder's gameplay paths (PartySearch 0x006E): the list the finder window loads, a listing made for a
// party the player already leads, and the refusals of the register and remove buttons.
public class PartySearchPathTests {
    private const int PartyId = 5;
    private const long ListingId = 7;

    private TestGameSession test = null!;
    private PartySearchHandler handler = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void Login() {
        test = new TestGameSession();
        test.Start();
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        session.Party = new PartyManager(test.World, session);
        handler = new PartySearchHandler { World = test.World, BanWordStorage = TestGameSession.BanWords() };
        test.TakeSent();
    }

    private void Add(string title, int size = 10, bool noApproval = true) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteUnicodeString(title);
            writer.WriteBool(noApproval);
            writer.WriteInt(size);
        }));
    }

    private void Remove() => handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));

    private void Load(string search, byte sort = (byte) PartySearchSort.Newest, int page = 1) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteByte(sort);
            writer.WriteUnicodeString(search);
            writer.WriteInt(page);
            writer.WriteInt(0);
        }));
    }

    // A party of the player and `others` more, led by the player or by the first other member.
    private PartyInfo PartyOf(bool listed, int others = 0, bool leader = true) {
        long leaderId = session.CharacterId;
        var members = new List<long> { session.CharacterId };
        for (int i = 1; i <= others; i++) {
            long id = session.CharacterId + 100 * i;
            test.World.AddPlayer(session.AccountId + 100 * i, id, "member" + i);
            members.Add(id);
        }
        if (!leader) {
            leaderId = members[1];
        }
        var info = new PartyInfo {
            Id = PartyId,
            LeaderAccountId = session.AccountId,
            LeaderCharacterId = leaderId,
            LeaderName = session.PlayerName,
            CreationTime = 1000,
        };
        if (listed) {
            info.PartySearch = new PartySearchInfo {
                Id = ListingId,
                PartyId = PartyId,
                Name = "come",
                Size = 10,
                MemberCount = members.Count,
                LeaderAccountId = session.AccountId,
                LeaderCharacterId = leaderId,
                LeaderName = session.PlayerName,
                CreationTime = 1000,
            };
        }
        foreach (long id in members) {
            info.Members.Add(new PartyInfo.Types.Member { CharacterId = id, JoinTime = 1000 });
        }
        return info;
    }

    private void Join(PartyInfo info) {
        Assert.That(session.Party.SetParty(info), Is.True);
        test.TakeSent();
    }

    private string[] Sent(SendOp op = SendOp.PartySearch) => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == op)
        .Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static PartySearchInfo Listing(long id, string name, int members) => new() {
        Id = id,
        PartyId = (int) id + 100,
        Name = name,
        Size = 10,
        MemberCount = members,
        NoApproval = id % 2 == 0,
        LeaderAccountId = id + 1000,
        LeaderCharacterId = id + 2000,
        LeaderName = "leader" + id,
        CreationTime = 5000 + id,
    };

    // The finder window's list: the World's page goes out as the Load, row by row in the World's order.
    [Test]
    public void Load_ListsTheWorldsPage() {
        PartySearchInfo[] rows = [Listing(11, "boss run", 3), Listing(12, "chat", 1)];
        test.World.PartySearchWorld = _ => new PartySearchResponse { PartySearches = { rows } };

        Load("run", (byte) PartySearchSort.LeastMembers, page: 2);

        PartySearchRequest request = test.World.PartySearches.Single();
        Assert.That(request.PartySearchCase, Is.EqualTo(PartySearchRequest.PartySearchOneofCase.Fetch));
        Assert.That(request.Fetch.Page, Is.EqualTo(2));
        Assert.That(request.Fetch.SearchString, Is.EqualTo("run"));
        Assert.That(request.Fetch.SortBy, Is.EqualTo((int) PartySearchSort.LeastMembers));
        PartySearch[] expected = rows.Select(row => new PartySearch(row.Id, row.Name, row.Size) {
            PartyId = row.PartyId,
            CreationTime = row.CreationTime,
            NoApproval = row.NoApproval,
            MemberCount = row.MemberCount,
            LeaderAccountId = row.LeaderAccountId,
            LeaderCharacterId = row.LeaderCharacterId,
            LeaderName = row.LeaderName,
        }).ToArray();
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Load(expected)) }));
    }

    [Test]
    public void Load_EmptyPage_ListsNothing() {
        Load("");

        Assert.That(test.World.PartySearches.Single().Fetch.SearchString, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Load(Array.Empty<PartySearch>())) }));
    }

    [Test]
    public void Load_WorldError_IsPassedOnWithItsType() {
        test.World.PartySearchWorld = _ => new PartySearchResponse {
            Error = (int) PartySearchError.s_partysearch_err_server_db,
            ErrorCategory = 2,
        };

        Load("run");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_db, 2)) }));
    }

    [Test]
    public void Load_UnknownSort_IsRefusedBeforeTheWorld() {
        Load("run", sort: 200);

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_invalid_type)) }));
    }

    // A leader lists the party they lead: no party is made, the World gets the title, size and approval choice.
    [Test]
    public void Add_LeaderOfAParty_ListsIt() {
        Join(PartyOf(false, others: 1));
        bool partyAsked = false;
        test.World.PartyWorld = _ => {
            partyAsked = true;
            return new PartyResponse();
        };

        Add("come along", noApproval: true);

        Assert.That(partyAsked, Is.False);
        PartySearchRequest request = test.World.PartySearches.Single();
        Assert.That(request.PartyId, Is.EqualTo(PartyId));
        Assert.That(request.Create.Name, Is.EqualTo("come along"));
        Assert.That(request.Create.Size, Is.EqualTo(10));
        Assert.That(request.Create.NoApproval, Is.True);
        Assert.That(request.Create.RequestorId, Is.EqualTo(session.CharacterId));
        Assert.That(Sent(), Is.Empty);
        Assert.That(session.Party.Party, Is.Not.Null);
    }

    // Without a party, one is made first and kept with the listing.
    [Test]
    public void Add_NoParty_MakesOneAndListsIt() {
        test.World.PartyWorld = request => {
            Assert.That(request.PartyCase, Is.EqualTo(PartyRequest.PartyOneofCase.Create));
            return new PartyResponse { Party = PartyOf(false) };
        };

        Add("come along");

        Assert.That(session.Party.Party?.Id, Is.EqualTo(PartyId));
        Assert.That(test.World.PartySearches.Single().PartyId, Is.EqualTo(PartyId));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void Add_PartyRefusedByTheWorld_PassesOnThePartyError() {
        test.World.PartyWorld = _ => new PartyResponse { Error = (int) PartyError.s_party_err_already };

        Add("come along");

        Assert.That(session.Party.Party, Is.Null);
        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(SendOp.Party), Is.EqualTo(new[] { Hex(PartyPacket.Error(PartyError.s_party_err_already)) }));
    }

    [Test]
    public void Add_AlreadyListed_IsRefused() {
        Join(PartyOf(true));

        Add("come along");

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_already_register)) }));
    }

    [Test]
    public void Add_NotTheLeader_IsRefused() {
        Join(PartyOf(false, others: 1, leader: false));

        Add("come along");

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_chief)) }));
    }

    // A size the party already fills.
    [Test]
    public void Add_PartyAlreadyFull_IsRefused() {
        Join(PartyOf(false, others: 9));

        Add("come along", size: 10);

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_max_member)) }));
    }

    // A one-letter title or a size the dialog does not offer.
    [TestCase("a", 10)]
    [TestCase("come along", 7777)]
    public void Add_NotFromTheDialog_IsRefused(string title, int size) {
        bool partyAsked = false;
        test.World.PartyWorld = _ => {
            partyAsked = true;
            return new PartyResponse();
        };

        Add(title, size);

        Assert.That(partyAsked, Is.False);
        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_invalid_type)) }));
    }

    // A listing of a party with others in it: the removal goes out and the party stays.
    [Test]
    public void Remove_PartyOfMany_RemovesTheListingOnly() {
        Join(PartyOf(true, others: 1));
        test.World.PartySearchWorld = _ => new PartySearchResponse();
        bool leaveAsked = false;
        test.World.PartyWorld = _ => {
            leaveAsked = true;
            return new PartyResponse();
        };

        Remove();

        PartySearchRequest request = test.World.PartySearches.Single();
        Assert.That(request.PartySearchCase, Is.EqualTo(PartySearchRequest.PartySearchOneofCase.Remove));
        Assert.That(request.Id, Is.EqualTo(ListingId));
        Assert.That(request.PartyId, Is.EqualTo(PartyId));
        Assert.That(leaveAsked, Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Remove(ListingId)) }));
    }

    // A lone leader's listing: the party made for it is left on the World.
    [Test]
    public void Remove_LoneLeader_LeavesTheParty() {
        Join(PartyOf(true));
        test.World.PartySearchWorld = _ => new PartySearchResponse();
        PartyRequest? leave = null;
        test.World.PartyWorld = request => {
            leave = request;
            return new PartyResponse();
        };

        Remove();

        Assert.That(leave?.Leave?.PartyId, Is.EqualTo(PartyId));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Remove(ListingId)) }));
    }

    [Test]
    public void Remove_NoParty_PartyNotFound() {
        Remove();

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(SendOp.Party), Is.EqualTo(new[] { Hex(PartyPacket.Error(PartyError.s_party_err_not_found)) }));
    }

    [Test]
    public void Remove_NotTheLeader_IsRefused() {
        Join(PartyOf(true, others: 1, leader: false));

        Remove();

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_chief)) }));
    }

    [Test]
    public void Remove_NotListed_NoRecruitType1() {
        Join(PartyOf(false));

        Remove();

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_find_recruit, 1)) }));
    }

    [Test]
    public void Remove_WorldError_KeepsTheListing() {
        Join(PartyOf(true));
        test.World.PartySearchWorld = _ => new PartySearchResponse {
            Error = (int) PartySearchError.s_partysearch_err_server_not_find_recruit,
            ErrorCategory = 1,
        };

        Remove();

        Assert.That(session.Party.Party!.Search, Is.Not.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_find_recruit, 1)) }));
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using ChannelPartyRequest = Maple2.Server.Channel.Service.PartyRequest;
using ChannelPartyResponse = Maple2.Server.Channel.Service.PartyResponse;

namespace Maple2.Server.Tests.World;

// The World's party create, invite, invite answer and leader change (Party 1, 2, 3, 0x11) through
// WorldService.Party as a channel calls it: who may invite, the invite the invitee answers, and what the members hear.
public class PartyServiceTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<ChannelPartyRequest> Requests = [];
        public int InviteAnswer;
        public bool Throw;

        public override ChannelPartyResponse Party(ChannelPartyRequest request, CallOptions options) {
            Requests.Add(request.Clone());
            if (Throw) {
                throw new RpcException(new Status(StatusCode.Unavailable, "channel down"));
            }
            return new ChannelPartyResponse { Error = request.Invite != null ? InviteAnswer : 0 };
        }
    }

    private TestGameSession test = null!;
    private FakeChannel channel = null!;
    private PlayerInfoLookup playerLookup = null!;
    private PartySearchLookup partySearch = null!;
    private PartyLookup lookup = null!;
    private WorldService world = null!;
    private const long Leader = 2001;
    private const long Guest = 2002;
    private const long Third = 2003;

    private static PlayerInfo Info(long characterId, string name) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 60), "",
            new AchievementInfo(), []) {
            Channel = 1,
        };
    }

    private static void AddChannel(ChannelClientLookup channels, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object entry = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var entries = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(channels)!;
        entries[id] = entry;
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var channels = new ChannelClientLookup();
        channel = new FakeChannel();
        AddChannel(channels, 1, channel);
        playerLookup = new PlayerInfoLookup(test.Storage, channels);
        var cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(playerLookup)!;
        cache[Leader] = Info(Leader, "Leader");
        cache[Guest] = Info(Guest, "Guest");
        cache[Third] = Info(Third, "Third");
        partySearch = new PartySearchLookup(channels);
        lookup = new PartyLookup(channels, playerLookup, partySearch, test.ServerTableMetadata);
        world = new WorldService(null!, null!, channels, playerLookup, null!, lookup, partySearch, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!);
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
        partySearch.Dispose();
        playerLookup.Dispose();
    }

    private PartyResponse Call(long requestorId, Action<PartyRequest> fill) {
        var request = new PartyRequest { RequestorId = requestorId };
        fill(request);
        channel.Requests.Clear();
        return world.Party(request, null!).GetAwaiter().GetResult();
    }

    private int Create() {
        PartyResponse response = Call(Leader, request => request.Create = new PartyRequest.Types.Create());
        Assert.That(response.Error, Is.Zero);
        return response.Party.Id;
    }

    private PartyResponse Invite(int partyId, long requestorId, long receiverId) =>
        Call(requestorId, request => request.Invite = new PartyRequest.Types.Invite { PartyId = partyId, ReceiverId = receiverId });

    private PartyResponse Respond(int partyId, long characterId, PartyInviteResponse reply) =>
        Call(characterId, request => request.RespondInvite = new PartyRequest.Types.RespondInvite { PartyId = partyId, Reply = (int) reply });

    [Test]
    public void Create_TheRequesterLeads() {
        int partyId = Create();

        Assert.That(lookup.TryGet(partyId, out PartyManager? manager), Is.True);
        Assert.That((manager!.Party.LeaderCharacterId, manager.Party.LeaderName), Is.EqualTo((Leader, "Leader")));
        Assert.That(manager.Party.Members.Keys, Is.EqualTo(new[] { Leader }));
    }

    // The leader's invite goes to the invitee's channel with the sender; an accept joins the party and the members
    // hear the new member.
    [Test]
    public void Invite_ThenAccept_Joins() {
        int partyId = Create();

        Assert.That(Invite(partyId, Leader, Guest).Error, Is.Zero);
        ChannelPartyRequest sent = channel.Requests.Single();
        Assert.That((sent.ReceiverIds.Single(), sent.Invite.SenderId, sent.Invite.SenderName), Is.EqualTo((Guest, Leader, "Leader")));

        PartyResponse joined = Respond(partyId, Guest, PartyInviteResponse.Accept);

        Assert.That(joined.Error, Is.Zero);
        Assert.That(joined.Party.Members.Select(member => member.CharacterId), Is.EquivalentTo(new[] { Leader, Guest }));
        Assert.That(channel.Requests.Single(request => request.AddMember != null).AddMember.CharacterId, Is.EqualTo(Guest));
    }

    // A rejection is told to the members with its reply; the invite is used up, so a later accept is refused.
    [Test]
    public void Invite_ThenReject_TellsTheMembers() {
        int partyId = Create();
        Invite(partyId, Leader, Guest);

        PartyResponse rejected = Respond(partyId, Guest, PartyInviteResponse.RejectInvite);

        Assert.That((rejected.Error, rejected.PartyId), Is.EqualTo((0, partyId)));
        ChannelPartyRequest reply = channel.Requests.Single();
        Assert.That((reply.InviteReply.Name, reply.InviteReply.Reply), Is.EqualTo(("Guest", (int) PartyInviteResponse.RejectInvite)));
        Assert.That(Respond(partyId, Guest, PartyInviteResponse.Accept).Error, Is.EqualTo((int) PartyError.s_party_err_cannot_invite));
        Assert.That(lookup.TryGet(partyId, out PartyManager? manager) && manager!.Party.Members.ContainsKey(Guest), Is.False);
    }

    // Not a member, a member who is not the leader, a player already in, an unknown player, an unknown party, and
    // the invitee's channel refusing (its own answer is passed on).
    [Test]
    public void Invite_Refusals() {
        int partyId = Create();
        Invite(partyId, Leader, Guest);
        Respond(partyId, Guest, PartyInviteResponse.Accept);

        Assert.Multiple(() => {
            Assert.That(Invite(partyId, Third, Third).Error, Is.EqualTo((int) PartyError.s_party_err_not_exist));
            Assert.That(Invite(partyId, Guest, Third).Error, Is.EqualTo((int) PartyError.s_party_err_not_chief));
            Assert.That(Invite(partyId, Leader, Guest).Error, Is.EqualTo((int) PartyError.s_party_err_cannot_invite));
            Assert.That(Invite(partyId, Leader, 99_999).Error, Is.EqualTo((int) PartyError.s_party_err_cannot_invite));
            Assert.That(Invite(partyId + 100, Leader, Third).Error, Is.EqualTo((int) PartyError.s_party_err_not_found));
        });

        channel.InviteAnswer = (int) PartyError.s_party_err_already;
        Assert.That(Invite(partyId, Leader, Third).Error, Is.EqualTo((int) PartyError.s_party_err_already));
    }

    // An answer with no invite open (none sent, or another party's) is refused.
    [Test]
    public void Respond_WithoutAnInvite_IsRefused() {
        int partyId = Create();

        Assert.That(Respond(partyId, Guest, PartyInviteResponse.Accept).Error, Is.EqualTo((int) PartyError.s_party_err_cannot_invite));
        Assert.That(Respond(partyId + 100, Guest, PartyInviteResponse.Accept).Error, Is.EqualTo((int) PartyError.s_party_err_not_found));
    }

    // Only the leader hands the party over, and only to a member; the members hear the new leader.
    [Test]
    public void UpdateLeader_ByTheLeaderToAMember() {
        int partyId = Create();
        Invite(partyId, Leader, Guest);
        Respond(partyId, Guest, PartyInviteResponse.Accept);

        PartyResponse Hand(long requestor, long to) => Call(requestor, request =>
            request.UpdateLeader = new PartyRequest.Types.UpdateLeader { PartyId = partyId, CharacterId = to });

        Assert.That(Hand(Guest, Guest).Error, Is.EqualTo((int) PartyError.s_party_err_not_chief));
        Assert.That(Hand(Leader, Third).Error, Is.EqualTo((int) PartyError.s_party_err_not_found));
        Assert.That(Hand(Leader, Guest).Error, Is.Zero);

        Assert.That(lookup.TryGet(partyId, out PartyManager? manager), Is.True);
        Assert.That((manager!.Party.LeaderCharacterId, manager.Party.LeaderName), Is.EqualTo((Guest, "Guest")));
        Assert.That(channel.Requests.Single().UpdateLeader.CharacterId, Is.EqualTo(Guest));
    }

    // An invite the invitee's channel refused or that did not reach it cannot be accepted: the invitee never saw it.
    // Before, the World kept it for 30 seconds either way, so a hand-made accept joined the party.
    [TestCase(false)]
    [TestCase(true)]
    public void UndeliveredInvite_CannotBeAccepted(bool channelDown) {
        int partyId = Create();
        if (channelDown) {
            channel.Throw = true;
        } else {
            channel.InviteAnswer = (int) PartyError.s_party_err_already;
        }

        Assert.That(Invite(partyId, Leader, Guest).Error, Is.Not.Zero);
        channel.Throw = false;

        Assert.That(Respond(partyId, Guest, PartyInviteResponse.Accept).Error, Is.EqualTo((int) PartyError.s_party_err_cannot_invite));
        Assert.That(lookup.TryGet(partyId, out PartyManager? manager) && manager!.Party.Members.ContainsKey(Guest), Is.False);
    }
}

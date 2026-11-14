using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using WorldClubRequest = Maple2.Server.World.Service.ClubRequest;
using WorldClubResponse = Maple2.Server.World.Service.ClubResponse;
using Club = Maple2.Model.Game.Club.Club;
using ClubResponse = Maple2.Server.Channel.Service.ClubResponse;
using GuildResponse = Maple2.Server.Channel.Service.GuildResponse;

namespace Maple2.Server.Tests.World;

// The invited player logs out with a guild or club invite still open. The client has a text for it: Guild 6 reason 2
// (guildmanager.cpp:1636-1650) and Club 9 code 2 (clubcli.cpp:1143-1160, 1411). Before, the World kept the invite
// until it expired and the inviter was never told.
public class InviteLogoutTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChannelId = 1;

    private sealed class Players(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    // A channel that keeps each request and can hand club requests on to a game server's channel service.
    private sealed class FakeChannel : ChannelClient {
        public readonly List<GuildRequest> GuildRequests = [];
        public readonly List<ClubRequest> ClubRequests = [];
        public ChannelService? Game;

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            GuildRequests.Add(request.Clone());
            return new GuildResponse();
        }

        public override ClubResponse Club(ClubRequest request, CallOptions options) {
            ClubRequests.Add(request.Clone());
            return Game?.Club(request, null!).GetAwaiter().GetResult() ?? new ClubResponse();
        }
    }

    private TestGameSession test = null!;
    private FakeChannel channel = null!;
    private ChannelClientLookup channels = null!;
    private long[] ids = null!;
    private Dictionary<long, PlayerInfo> infos = null!;

    private static PlayerInfo Info(long characterId, string name) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = ChannelId,
        };
    }

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object entry = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var entries = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        entries[id] = entry;
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = _ => new Ms2Context(test.Options);
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property with { Level = 1, Capacity = 10 } }));
        ids = [test.CharacterId, test.CharacterId + 500, test.CharacterId + 501];
        infos = new Dictionary<long, PlayerInfo> {
            [ids[0]] = Info(ids[0], "Leader"),
            [ids[1]] = Info(ids[1], "Second"),
            [ids[2]] = Info(ids[2], "Guest"),
        };
        channels = new ChannelClientLookup();
        channel = new FakeChannel();
        AddChannel(channels, ChannelId, channel);
    }

    [TearDown]
    public void TearDown() {
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
    }

    private GuildManager Guild() {
        Guild guild;
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Guild", ids[0]) ?? throw new InvalidOperationException("guild row not created");
        }
        guild.Members.Clear();
        guild.Members[ids[0]] = new GuildMember { GuildId = guild.Id, Info = infos[ids[0]], Rank = 0, JoinTime = 1000 };
        guild.Members[ids[1]] = new GuildMember { GuildId = guild.Id, Info = infos[ids[1]], Rank = 1, JoinTime = 1000 };
        return new GuildManager(guild) {
            GameStorage = test.Storage,
            ChannelClients = channels,
            TableMetadata = test.TableMetadata,
            ItemMetadata = test.ItemMetadata,
            ServerTableMetadata = test.ServerTableMetadata,
        };
    }

    private ClubManager Club() {
        Club club;
        using (GameStorage.Request db = test.Storage.Context()) {
            club = db.CreateClub(new Players(infos), "Club", ids[0], [infos[ids[0]], infos[ids[1]]])!;
        }
        Assert.That(club, Is.Not.Null);
        club.State = ClubState.Established;
        return new ClubManager(club) {
            GameStorage = test.Storage,
            ChannelClients = channels,
        };
    }

    [Test]
    public void Guild_OpenInvite_TheMembersGetReason2() {
        GuildManager manager = Guild();
        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(GuildError.none));
        channel.GuildRequests.Clear();

        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.True);

        GuildRequest sent = channel.GuildRequests.Single();
        Assert.Multiple(() => {
            Assert.That(sent.InviteReply.Name, Is.EqualTo("Guest"));
            Assert.That(sent.InviteReply.Reply, Is.EqualTo((int) GuildInvite.Response.RejectLogout));
            Assert.That(sent.ReceiverIds, Is.EquivalentTo(ids.Take(2)));
        });
        // The invite is gone: a second logout tells nobody, and a late answer finds no invite.
        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.False);
        Assert.That(manager.ConsumeInvite(ids[2]), Is.Empty);
        Assert.That(channel.GuildRequests, Has.Count.EqualTo(1));
    }

    [Test]
    public void Guild_NoOpenInvite_NothingIsSent() {
        GuildManager manager = Guild();
        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(GuildError.none));
        Assert.That(manager.ConsumeInvite(ids[2]), Is.EqualTo("Leader"));
        channel.GuildRequests.Clear();

        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.False);
        Assert.That(manager.InviteeLoggedOut(ids[1], "Second"), Is.False);
        Assert.That(channel.GuildRequests, Is.Empty);
    }

    [Test]
    public void Guild_PacketCarriesReason2() {
        byte[] packet = Maple2.Server.Game.Packets.GuildPacket.NotifyInvite("Guest", GuildInvite.Response.RejectLogout).ToArray();
        // Guild 6: name, accepted byte, reason byte (guildmanager.cpp:1636-1650).
        Assert.That(packet[^1], Is.EqualTo(2));
    }

    // End to end: the World's notice reaches a member's game session as Club 9 with code 2.
    [Test]
    public void Club_OpenInvite_TheMembersGetCode2() {
        ClubManager manager = Club();
        GameSession session = test.Start();
        Assert.That(session.CharacterId, Is.EqualTo(ids[0]));
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session };
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(ClubError.none));
        // The members' channel is wired to the game only now: the invitee has no session here.
        channel.Game = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        channel.ClubRequests.Clear();
        test.TakeSent();

        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.True);

        ClubRequest sent = channel.ClubRequests.Single();
        Assert.Multiple(() => {
            Assert.That(sent.InviteReply.RequestorName, Is.EqualTo("Guest"));
            Assert.That(sent.InviteReply.Accept, Is.False);
            Assert.That(sent.InviteReply.Code, Is.EqualTo(2));
            Assert.That(sent.ReceiverIds, Is.EquivalentTo(ids.Take(2)));
        });
        byte[][] clubPackets = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Club).ToArray();
        Assert.That(clubPackets.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(Maple2.Server.Game.Packets.ClubPacket.InviteNotification(manager.Club.Id, "Guest", false, 2).ToArray()),
        }));
        Assert.That(clubPackets[0][^1], Is.EqualTo(2));
        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.False);
        Assert.That(manager.ConsumeInvite(ids[2]), Is.Empty);
    }

    // The World's logout (UpdatePlayer with channel -1) asks every guild and club held: only the inviting one sends.
    [Test]
    public void Lookups_OnlyTheInvitingGuildAndClubSend() {
        var playerLookup = new PlayerInfoLookup(test.Storage, channels);
        var guilds = new GuildLookup(test.Storage, test.TableMetadata, test.ItemMetadata, test.ServerTableMetadata, channels, playerLookup);
        var clubs = new ClubLookup(channels, playerLookup, test.Storage, null!);
        GuildManager guild = Guild();
        ClubManager club = Club();
        ((ConcurrentDictionary<long, GuildManager>) typeof(GuildLookup).GetField("guilds", Any)!.GetValue(guilds)!)[guild.Guild.Id] = guild;
        ((ConcurrentDictionary<long, ClubManager>) typeof(ClubLookup).GetField("clubs", Any)!.GetValue(clubs)!)[club.Club.Id] = club;
        Assert.That(guild.Invite(ids[0], infos[ids[2]]), Is.EqualTo(GuildError.none));
        Assert.That(club.Invite(ids[0], infos[ids[2]]), Is.EqualTo(ClubError.none));
        channel.GuildRequests.Clear();
        channel.ClubRequests.Clear();

        guilds.InviteeLoggedOut(ids[1], "Second");
        clubs.InviteeLoggedOut(ids[1], "Second");
        Assert.That(channel.GuildRequests, Is.Empty);
        Assert.That(channel.ClubRequests, Is.Empty);

        guilds.InviteeLoggedOut(ids[2], "Guest");
        clubs.InviteeLoggedOut(ids[2], "Guest");
        Assert.That(channel.GuildRequests.Single().InviteReply.Reply, Is.EqualTo((int) GuildInvite.Response.RejectLogout));
        Assert.That(channel.ClubRequests.Single().InviteReply.Code, Is.EqualTo(2));
        playerLookup.Dispose();
    }

    [Test]
    public void Club_NoOpenInvite_NothingIsSent() {
        ClubManager manager = Club();

        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.False);
        Assert.That(channel.ClubRequests, Is.Empty);
    }

    // A guild invite to a player who is offline (channel -1) or no longer on the channel: the client's "player not
    // found" text, s_guild_err_null_user (10). 5 (s_guild_err_wait_inviting) is its "waiting for an answer" text.
    [Test]
    public void Guild_OfflineInvitee_NullUser() {
        GuildManager manager = Guild();
        infos[ids[2]].Channel = -1;

        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(GuildError.s_guild_err_null_user));
        Assert.That(channel.GuildRequests, Is.Empty);
        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.False);
    }

    [Test]
    public void Guild_InviteeGoneFromTheChannel_NullUser() {
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession>());
        var service = new ChannelService(server, null!, test.Storage, null!, null!, null!);

        GuildResponse response = service.Guild(new GuildRequest {
            GuildId = 1,
            ReceiverIds = { ids[2] },
            Invite = new GuildRequest.Types.Invite { GuildName = "Guild", SenderId = ids[0], SenderName = "Leader" },
        }, null!).GetAwaiter().GetResult();

        Assert.That(response.Error, Is.EqualTo((int) GuildError.s_guild_err_null_user));
    }

    // A club invite to an offline player (channel -1): s_club_err_null_user (11), the client's "player not found"
    // text (0x372, clubcli.cpp:1430-1500), not s_club_err_unknown (1).
    [Test]
    public void Club_OfflineInvitee_NullUser() {
        ClubManager manager = Club();
        infos[ids[2]].Channel = -1;

        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(ClubError.s_club_err_null_user));
        Assert.That(channel.ClubRequests, Is.Empty);
        Assert.That(manager.InviteeLoggedOut(ids[2], "Guest"), Is.False);
    }

    // The invitee's answer to a club invite (World Club RespondInvite), as the World server takes it.
    private WorldClubResponse Respond(ClubManager manager, bool accept) {
        var playerLookup = new PlayerInfoLookup(test.Storage, channels);
        var cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(playerLookup)!;
        foreach ((long id, PlayerInfo info) in infos) {
            cache[id] = info;
        }
        var clubs = new ClubLookup(channels, playerLookup, test.Storage, null!);
        ((ConcurrentDictionary<long, ClubManager>) typeof(ClubLookup).GetField("clubs", Any)!.GetValue(clubs)!)[manager.Club.Id] = manager;
        var world = new Maple2.Server.World.Service.WorldService(null!, null!, channels, playerLookup, null!, null!, null!, null!, null!, clubs, null!, null!, null!,
            null!, null!, null!, null!, null!);
        try {
            return world.Club(new WorldClubRequest {
                RequestorId = ids[2],
                RespondInvite = new WorldClubRequest.Types.RespondInvite { ClubId = manager.Club.Id, Accept = accept },
            }, null!).GetAwaiter().GetResult();
        } finally {
            playerLookup.Dispose();
        }
    }

    // An accept the join then refuses (here the club filled up while the invite was open): the members were told the
    // invitee accepted (Club 9, accept 1) and no one joined. Now the member is stored first and a refused join sends
    // nothing to the members; the invitee gets the error.
    [Test]
    public void Club_AcceptTheJoinRefuses_NoAcceptIsSent() {
        ClubManager manager = Club();
        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(ClubError.none));
        for (int i = manager.Club.Members.Count; i < Constant.MaxClubMembers; i++) {
            long id = ids[0] + 600 + i;
            manager.Club.Members[id] = new Maple2.Model.Game.Club.ClubMember { ClubId = manager.Club.Id, Info = Info(id, $"Filler{i}") };
        }
        channel.ClubRequests.Clear();

        WorldClubResponse response = Respond(manager, accept: true);

        Assert.That(response.Error, Is.EqualTo((int) ClubError.s_club_err_full_member));
        Assert.That(channel.ClubRequests, Is.Empty);
        Assert.That(manager.Club.Members.ContainsKey(ids[2]), Is.False);
    }

    // An accept that joins: the accepted reply (Club 9) and then the join notice, both after the member is stored.
    [Test]
    public void Club_Accept_ReplyThenJoinNotice() {
        ClubManager manager = Club();
        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(ClubError.none));
        channel.ClubRequests.Clear();

        WorldClubResponse response = Respond(manager, accept: true);

        Assert.That(response.Error, Is.EqualTo(0));
        Assert.That(channel.ClubRequests.Select(request => request.ClubCase),
            Is.EqualTo(new[] { ClubRequest.ClubOneofCase.InviteReply, ClubRequest.ClubOneofCase.AddMember }));
        Assert.That(channel.ClubRequests[0].InviteReply.Accept, Is.True);
        Assert.That(channel.ClubRequests[0].InviteReply.RequestorName, Is.EqualTo("Guest"));
        Assert.That(manager.Club.Members.ContainsKey(ids[2]), Is.True);
    }

    [Test]
    public void Club_Decline_ReplyOnly() {
        ClubManager manager = Club();
        Assert.That(manager.Invite(ids[0], infos[ids[2]]), Is.EqualTo(ClubError.none));
        channel.ClubRequests.Clear();

        WorldClubResponse response = Respond(manager, accept: false);

        Assert.That(response.Error, Is.EqualTo(0));
        Assert.That(channel.ClubRequests.Single().InviteReply.Accept, Is.False);
        Assert.That(manager.Club.Members.ContainsKey(ids[2]), Is.False);
    }
}

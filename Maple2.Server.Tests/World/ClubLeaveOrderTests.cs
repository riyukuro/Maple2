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
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Maple2.Model.Error;
using WorldClubRequest = Maple2.Server.World.Service.ClubRequest;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using Club = Maple2.Model.Game.Club.Club;
using GameClubManager = Maple2.Server.Game.Manager.ClubManager;
using WorldClubInfo = Maple2.ClubInfo;
using ClubResponse = Maple2.Server.Channel.Service.ClubResponse;
using ClubRequest = Maple2.Server.Channel.Service.ClubRequest;

namespace Maple2.Server.Tests.World;

// Club 6 (leave), the leader change and the rename on the World: only a member leaves or disbands a club of two (a
// stale request from no member changes nothing), and a new leader or name is stored before the members are told
// (a failed write keeps the old one).
public class ClubLeaveOrderTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChannelId = 1;

    private sealed class Players(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    // A channel that keeps each club request and can hand it on to a game server's channel service.
    private sealed class FakeChannel : ChannelClient {
        public readonly List<ClubRequest> Requests = [];
        public ChannelService? Game;

        public override ClubResponse Club(ClubRequest request, CallOptions options) {
            Requests.Add(request.Clone());
            return Game?.Club(request, null!).GetAwaiter().GetResult() ?? new ClubResponse();
        }
    }

    private TestGameSession test = null!;
    private FakeChannel channel = null!;
    private PlayerInfoLookup playerLookup = null!;
    private ClubLookup lookup = null!;
    private WorldService service = null!;
    private Dictionary<long, PlayerInfo> infos = null!;
    private long[] ids = null!;

    private static PlayerInfo Info(long characterId, string name) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = ChannelId,
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
        // Member and club rows go by batch deletes, which need a context they can open on the test database.
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = _ => new Ms2Context(test.Options);
        ids = [test.CharacterId, test.CharacterId + 500, test.CharacterId + 501, test.CharacterId + 502];
        infos = new Dictionary<long, PlayerInfo> {
            [ids[0]] = Info(ids[0], "Leader"),
            [ids[1]] = Info(ids[1], "Second"),
            [ids[2]] = Info(ids[2], "Third"),
            [ids[3]] = Info(ids[3], "Fourth"),
        };
        var channels = new ChannelClientLookup();
        channel = new FakeChannel();
        AddChannel(channels, ChannelId, channel);
        playerLookup = new PlayerInfoLookup(test.Storage, channels);
        var cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(playerLookup)!;
        foreach ((long id, PlayerInfo info) in infos) {
            cache[id] = info;
        }
        lookup = new ClubLookup(channels, playerLookup, test.Storage, null!);
        service = (WorldService) RuntimeHelpers.GetUninitializedObject(typeof(WorldService));
        typeof(WorldService).GetField("clubLookup", Any)!.SetValue(service, lookup);
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
        playerLookup.Dispose();
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
    }

    // A stored club of the given members (the first leads), established unless staged.
    private long StoreClub(string name, bool staged, params long[] members) {
        Club club;
        using (GameStorage.Request db = test.Storage.Context()) {
            club = db.CreateClub(new Players(infos), name, members[0], members.Select(id => infos[id]).ToList())!;
        }
        Assume.That(club, Is.Not.Null);
        if (!staged) {
            using var context = new Ms2Context(test.Options);
            context.Club.Find(club.Id)!.State = ClubState.Established;
            context.SaveChanges();
        }
        return club.Id;
    }

    private Club? Stored(long clubId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetClub(playerLookup, clubId);
    }

    private int Leave(long requesterId, long clubId) {
        return service.Club(new WorldClubRequest {
            RequestorId = requesterId,
            Leave = new WorldClubRequest.Types.Leave { ClubId = clubId },
        }, null!).GetAwaiter().GetResult().Error;
    }

    private void DropClubRow(long clubId) {
        using var context = new Ms2Context(test.Options);
        context.Club.Remove(context.Club.Single(club => club.Id == clubId));
        context.SaveChanges();
    }

    [Test]
    public void NoMember_DoesNotDisbandAClubOfTwo() {
        long clubId = StoreClub("Club", false, ids[0], ids[1]);

        Assert.That(Leave(ids[2], clubId), Is.EqualTo((int) ClubError.s_club_err_null_member));

        Club? stored = Stored(clubId);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Members.Keys, Is.EquivalentTo(ids.Take(2)));
        Assert.That(channel.Requests, Is.Empty);
    }

    [Test]
    public void MemberOfTwo_DisbandsTheClub() {
        long clubId = StoreClub("Club", false, ids[0], ids[1]);

        Assert.That(Leave(ids[1], clubId), Is.EqualTo(0));

        Assert.That(Stored(clubId), Is.Null);
        Assert.That(channel.Requests.Select(request => request.ClubCase), Is.EqualTo(new[] { ClubRequest.ClubOneofCase.Disband }));
    }

    // The second leave of a club already disbanded finds no club.
    [Test]
    public void SecondLeave_AfterTheDisband_FindsNoClub() {
        long clubId = StoreClub("Club", false, ids[0], ids[1]);
        Assert.That(Leave(ids[1], clubId), Is.EqualTo(0));

        Assert.That(Leave(ids[0], clubId), Is.EqualTo((int) ClubError.s_club_err_null_club));
        Assert.That(channel.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void Rename_IsStored() {
        long clubId = StoreClub("Club", false, ids[0], ids[1], ids[2]);
        Assume.That(lookup.TryGet(clubId, out ClubManager? manager), Is.True);

        Assert.That(manager!.Rename("Renamed"), Is.EqualTo(ClubError.none));

        Assert.That(Stored(clubId)!.Name, Is.EqualTo("Renamed"));
    }

    [Test]
    public void Rename_FailedSave_KeepsTheName() {
        long clubId = StoreClub("Club", false, ids[0], ids[1], ids[2]);
        Assume.That(lookup.TryGet(clubId, out ClubManager? manager), Is.True);
        DropClubRow(clubId);

        Assert.That(manager!.Rename("Renamed"), Is.EqualTo(ClubError.s_club_err_unknown));

        Assert.That(manager.Club.Name, Is.EqualTo("Club"));
        Assert.That(channel.Requests, Is.Empty);
    }

    [Test]
    public void ChangeLeader_FailedSave_KeepsTheLeader() {
        long clubId = StoreClub("Club", false, ids[0], ids[1], ids[2]);
        Assume.That(lookup.TryGet(clubId, out ClubManager? manager), Is.True);
        DropClubRow(clubId);

        Assert.That(manager!.ChangeLeader(ids[0], "Second"), Is.EqualTo(ClubError.s_club_err_unknown));

        Assert.That(manager.Club.LeaderId, Is.EqualTo(ids[0]));
        Assert.That(manager.Club.Leader.CharacterId, Is.EqualTo(ids[0]));
        Assert.That(channel.Requests, Is.Empty);
    }
}

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
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using Club = Maple2.Model.Game.Club.Club;
using GameClubManager = Maple2.Server.Game.Manager.ClubManager;
using WorldClubInfo = Maple2.ClubInfo;
using ClubResponse = Maple2.Server.Channel.Service.ClubResponse;

namespace Maple2.Server.Tests.World;

// The World takes a character that is about to be deleted out of its clubs (ClubLookup.RemoveCharacter): a club of
// three or more hands a led club on and tells the others with Club 0x1c, a club of two and a staged club are
// disbanded. No club stops the deletion: one that fails is logged and the next club is still handled.
public class ClubDeleteCharacterTests {
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

    private ClubRequest.ClubOneofCase[] Cases() => channel.Requests.Select(request => request.ClubCase).ToArray();

    [Test]
    public void LeaderOfThree_HandsTheClubOnAndTheOthersHearItWasADeletion() {
        long clubId = StoreClub("Club", false, ids[0], ids[1], ids[2]);

        lookup.RemoveCharacter(ids[0]);

        Club? stored = Stored(clubId);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Members.Keys, Is.EquivalentTo(ids.Skip(1).Take(2)));
        Assert.That(stored.LeaderId, Is.Not.EqualTo(ids[0]));
        Assert.That(Cases(), Is.EqualTo(new[] { ClubRequest.ClubOneofCase.UpdateLeader, ClubRequest.ClubOneofCase.RemoveMember }));
        ClubRequest.Types.RemoveMember remove = channel.Requests[1].RemoveMember;
        Assert.That(remove.CharacterId, Is.EqualTo(ids[0]));
        Assert.That(remove.Deleted, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClubOfTwoOrStaged_IsDisbanded(bool staged) {
        long clubId = staged ? StoreClub("Club", true, ids[0], ids[1], ids[2]) : StoreClub("Club", false, ids[0], ids[1]);

        lookup.RemoveCharacter(ids[1]);

        Assert.That(Stored(clubId), Is.Null);
        Assert.That(Cases(), Is.EqualTo(new[] { ClubRequest.ClubOneofCase.Disband }));
        Assert.That(lookup.TryGet(clubId, out _), Is.False);
    }

    // A club stored with a leader who is no member loads with its longest member as the leader, and the deletion
    // of one of its members goes through.
    [Test]
    public void StoredLeaderNoMember_TheClubStillLoads() {
        long clubId = StoreClub("Club", false, ids[0], ids[1], ids[2], ids[3]);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assume.That(db.DeleteClubMember(clubId, ids[0]), Is.True);
        }

        lookup.RemoveCharacter(ids[3]);

        Club? stored = Stored(clubId);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Members.Keys, Is.EquivalentTo(ids.Skip(1).Take(2)));
        Assert.That(stored.Members.Keys, Does.Contain(stored.LeaderId));
        Assert.That(stored.Leader.CharacterId, Is.EqualTo(stored.LeaderId));
    }

    // The first club's disband fails (its row is already gone): it is logged and forgotten, nothing is broadcast for
    // it, and the second club still takes the character out.
    [Test]
    public void FailingClub_DoesNotStopTheNext() {
        long failing = StoreClub("Failing", false, ids[0], ids[1]);
        long other = StoreClub("Other", false, ids[1], ids[2], ids[3]);
        Assume.That(lookup.TryGet(failing, out _), Is.True);
        using (var context = new Ms2Context(test.Options)) {
            context.Club.Remove(context.Club.Single(club => club.Id == failing));
            context.SaveChanges();
        }

        Assert.DoesNotThrow(() => lookup.RemoveCharacter(ids[1]));

        Assert.That(lookup.TryGet(failing, out _), Is.False);
        Assert.That(channel.Requests.Select(request => request.ClubId), Has.None.EqualTo(failing));
        Assert.That(Stored(other)!.Members.Keys, Is.EquivalentTo(ids.Skip(2)));
    }

    // End to end: the World's RemoveMember with Deleted reaches a member's game session as Club 0x1c.
    [Test]
    public void Deletion_ReachesTheMembersGameSessionAs0x1c() {
        long clubId = StoreClub("Club", false, ids[0], ids[1], ids[2]);
        GameSession session = test.Start();
        Assume.That(session.CharacterId, Is.EqualTo(ids[0]));
        test.SetTable(new ClubBuffTable(new Dictionary<int, ClubBuffTable.Entry>()));
        foreach (long id in ids.Take(3)) {
            test.World.AddPlayer(infos[id].AccountId, id, infos[id].Name);
        }
        GameClubManager? manager = GameClubManager.Create(new WorldClubInfo {
            Id = clubId,
            Name = "Club",
            LeaderId = ids[0],
            LeaderName = "Leader",
            State = (int) ClubState.Established,
            Members = {
                ids.Take(3).Select(id => new WorldClubInfo.Types.Member { CharacterId = id, CharacterName = infos[id].Name }),
            },
        }, session);
        Assume.That(manager, Is.Not.Null);
        session.Clubs ??= new ConcurrentDictionary<long, GameClubManager>();
        session.Clubs[clubId] = manager!;
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session };
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        channel.Game = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        test.TakeSent();

        lookup.RemoveCharacter(ids[2]);

        Assert.That(manager!.Club.Members.Keys, Is.EquivalentTo(ids.Take(2)));
        byte[][] clubPackets = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Club).ToArray();
        Assert.That(clubPackets, Has.Length.EqualTo(1));
        Assert.That(clubPackets[0][2], Is.EqualTo(0x1c));
    }
}

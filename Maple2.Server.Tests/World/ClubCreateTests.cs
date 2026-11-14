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
using Maple2.PacketLib.Tools;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using Club = Maple2.Model.Game.Club.Club;
using WorldClubInfo = Maple2.ClubInfo;
using WorldClubManager = Maple2.Server.World.Containers.ClubManager;
using ClubRequest = Maple2.Server.Channel.Service.ClubRequest;
using ClubResponse = Maple2.Server.Channel.Service.ClubResponse;

namespace Maple2.Server.Tests.World;

// A party's club, end to end: the World stores it staged and sends Club 2 to the members' channels, each client
// answers with c2s 3 (the leader's on its own, the others from a 20 s box), and the club is established with
// Club 0xf code 0 (CClubCli::FUN_140b12ad0 sets state 2, the only state the Clubs window lists) or removed with
// Club 0xf code 76 and Club 5. A member the channel does not hold does not stop the members after it.
public class ClubCreateTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChannelId = 1;

    private sealed class Players(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    private sealed class FakeChannel : ChannelClient {
        public readonly List<ClubRequest> Requests = [];
        public readonly List<ClubResponse> Responses = [];
        public ChannelService? Game;

        public override ClubResponse Club(ClubRequest request, CallOptions options) {
            Requests.Add(request.Clone());
            ClubResponse response = Game?.Club(request, null!).GetAwaiter().GetResult() ?? new ClubResponse();
            Responses.Add(response);
            return response;
        }
    }

    private TestGameSession test = null!;
    private FakeChannel channel = null!;
    private PlayerInfoLookup playerLookup = null!;
    private ClubLookup lookup = null!;
    private Dictionary<long, PlayerInfo> infos = null!;
    private GameSession session => test.Session;
    private long leader;
    private long member;

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
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = _ => new Ms2Context(test.Options);
        leader = test.CharacterId;
        member = test.CharacterId + 500;
        infos = new Dictionary<long, PlayerInfo> {
            [leader] = Info(leader, "Leader"),
            [member] = Info(member, "Member"),
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

        // The game server holds only the leader's session; the member plays on another server of the same channel
        // id or is between two maps.
        test.Start();
        session.Clubs ??= new ConcurrentDictionary<long, Maple2.Server.Game.Manager.ClubManager>();
        Assume.That(session.CharacterId, Is.EqualTo(leader));
        test.SetTable(new ClubBuffTable(new Dictionary<int, ClubBuffTable.Entry>()));
        foreach ((long id, PlayerInfo info) in infos) {
            test.World.AddPlayer(info.AccountId, id, info.Name);
        }
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [leader] = session });
        channel.Game = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
        playerLookup.Dispose();
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
    }

    // The party's club as ClubLookup.Create stores it: staged, the requester leads.
    private WorldClubManager StagedClub() {
        Club? club;
        using (GameStorage.Request db = test.Storage.Context()) {
            club = db.CreateClub(new Players(infos), "Club", leader, [infos[leader], infos[member]]);
        }
        Assume.That(club, Is.Not.Null);
        Assume.That(club!.State, Is.EqualTo(ClubState.Staged));
        Assume.That(lookup.TryGet(club.Id, out WorldClubManager? manager), Is.True);
        return manager!;
    }

    private static WorldClubInfo ToClubInfo(Club club) {
        return (WorldClubInfo) typeof(WorldService).GetMethod("ToClubInfo", Any)!.Invoke(null, [club])!;
    }

    private byte[][] ClubPackets() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Club).ToArray();

    // Club 2 read as CClubCli::Read and CClubMember::Read take it (clubcli.cpp), to the last byte.
    private static (long Id, string LeaderName, byte State, int Members) ReadCreate(byte[] packet) {
        var reader = new ByteReader(packet, 3);
        long id = reader.ReadLong();
        reader.ReadUnicodeString(); // name
        reader.ReadLong(); // leader account
        reader.ReadLong(); // leader character
        string leaderName = reader.ReadUnicodeString();
        reader.ReadLong(); // created
        byte state = reader.ReadByte();
        reader.ReadInt(); // buff
        reader.ReadInt(); // buff level
        reader.ReadLong(); // name change
        byte count = reader.ReadByte();
        for (int i = 0; i < count; i++) {
            Assert.That(reader.ReadByte(), Is.EqualTo(2), "member type");
            reader.ReadLong(); // club id
            reader.ReadLong(); // account
            reader.ReadLong(); // character
            reader.ReadUnicodeString(); // name
            reader.ReadByte(); // gender
            reader.ReadInt(); // job code
            reader.ReadInt(); // job
            reader.ReadShort(); // level
            reader.ReadInt(); // map
            reader.ReadShort(); // channel
            reader.ReadUnicodeString(); // picture
            reader.ReadInt(); // plot map
            reader.ReadInt(); // plot number
            reader.ReadInt(); // apartment
            reader.ReadLong(); // plot expiry
            reader.ReadInt(); // trophies
            reader.ReadInt();
            reader.ReadInt();
            reader.ReadLong(); // join time
            reader.ReadLong(); // last online
            reader.ReadByte(); // offline
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return (id, leaderName, state, count);
    }

    // Club 0xf: the club, the code (0 accepted by all, else the refusal), the member's name.
    private static (long Id, int Code) ReadInviteReply(byte[] packet) {
        var reader = new ByteReader(packet, 3);
        long id = reader.ReadLong();
        int code = reader.ReadInt();
        reader.ReadUnicodeString();
        Assert.That(reader.Available, Is.EqualTo(0));
        return (id, code);
    }

    [Test]
    public void Create_TheLeaderGetsTheStagedClub() {
        WorldClubManager manager = StagedClub();

        manager.Broadcast(new ClubRequest {
            Create = new ClubRequest.Types.Create { Info = ToClubInfo(manager.Club) },
            ClubId = manager.Club.Id,
        });

        Assert.That(channel.Requests.Single().ReceiverIds, Is.EquivalentTo(new[] { leader, member }));
        byte[][] packets = ClubPackets();
        Assert.That(packets, Has.Length.EqualTo(1));
        Assert.That(packets[0][2], Is.EqualTo(2));
        (long id, string leaderName, byte state, int members) = ReadCreate(packets[0]);
        Assert.That(id, Is.EqualTo(manager.Club.Id));
        Assert.That(leaderName, Is.EqualTo("Leader"), "the client accepts on its own when this is its name");
        Assert.That(state, Is.EqualTo((byte) ClubState.Staged), "the client sends c2s 3 only for state 1");
        Assert.That(members, Is.EqualTo(2));
        Assert.That(session.Clubs.ContainsKey(id), Is.True);
    }

    // The member the channel does not hold comes first: the leader after it still gets Club 2.
    [Test]
    public void Create_AMissingMemberDoesNotStopTheNext() {
        WorldClubManager manager = StagedClub();

        channel.Game!.Club(new ClubRequest {
            Create = new ClubRequest.Types.Create { Info = ToClubInfo(manager.Club) },
            ClubId = manager.Club.Id,
            ReceiverIds = { member, leader },
        }, null!).GetAwaiter().GetResult();

        byte[][] packets = ClubPackets();
        Assert.That(packets.Select(packet => packet[2]), Is.EqualTo(new byte[] { 2 }));
        Assert.That(session.Clubs.ContainsKey(manager.Club.Id), Is.True);
    }

    // Establish with the member the channel does not hold first: the leader still gets Club 0xf code 0.
    [Test]
    public void Establish_AMissingMemberDoesNotStopTheNext() {
        WorldClubManager manager = StagedClub();
        long clubId = manager.Club.Id;
        channel.Game!.Club(new ClubRequest {
            Create = new ClubRequest.Types.Create { Info = ToClubInfo(manager.Club) },
            ClubId = clubId,
            ReceiverIds = { leader },
        }, null!).GetAwaiter().GetResult();
        test.TakeSent();

        channel.Game.Club(new ClubRequest {
            Establish = new ClubRequest.Types.Establish(),
            ClubId = clubId,
            ReceiverIds = { member, leader },
        }, null!).GetAwaiter().GetResult();

        byte[] reply = ClubPackets().Single(packet => packet[2] == 0xf);
        Assert.That(ReadInviteReply(reply), Is.EqualTo((clubId, 0)));
        Assert.That(session.Clubs[clubId].Club.State, Is.EqualTo(ClubState.Established));
    }

    [Test]
    public void AllAccept_TheClubIsEstablished() {
        WorldClubManager manager = StagedClub();
        manager.Broadcast(new ClubRequest {
            Create = new ClubRequest.Types.Create { Info = ToClubInfo(manager.Club) },
            ClubId = manager.Club.Id,
        });
        test.TakeSent();

        Assert.That(manager.NewClubInvite(leader, Model.Enum.ClubResponse.Accept), Is.EqualTo(Model.Error.ClubError.none));
        Assert.That(ClubPackets(), Is.Empty, "nothing until every member has accepted");
        Assert.That(manager.NewClubInvite(member, Model.Enum.ClubResponse.Accept), Is.EqualTo(Model.Error.ClubError.none));

        byte[][] packets = ClubPackets();
        byte[] reply = packets.Single(packet => packet[2] == 0xf);
        Assert.That(ReadInviteReply(reply), Is.EqualTo((manager.Club.Id, 0)));
        Assert.That(session.Clubs[manager.Club.Id].Club.State, Is.EqualTo(ClubState.Established));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetClub(playerLookup, manager.Club.Id)!.State, Is.EqualTo(ClubState.Established));
    }

    // The member's 20 s box timed out or was declined (c2s 3 with 76): the leader hears it and the club goes.
    [Test]
    public void MemberRejects_TheLeaderLosesTheStagedClub() {
        WorldClubManager manager = StagedClub();
        long clubId = manager.Club.Id;
        manager.Broadcast(new ClubRequest {
            Create = new ClubRequest.Types.Create { Info = ToClubInfo(manager.Club) },
            ClubId = clubId,
        });
        Assume.That(manager.NewClubInvite(leader, Model.Enum.ClubResponse.Accept), Is.EqualTo(Model.Error.ClubError.none));
        test.TakeSent();

        Assert.That(manager.NewClubInvite(member, Model.Enum.ClubResponse.Reject), Is.EqualTo(Model.Error.ClubError.none));
        Assert.That(lookup.Disband(clubId), Is.EqualTo(Model.Error.ClubError.none));

        byte[][] packets = ClubPackets();
        Assert.That(ReadInviteReply(packets.First(packet => packet[2] == 0xf)), Is.EqualTo((clubId, 76)));
        Assert.That(packets.Select(packet => packet[2]), Does.Contain((byte) 5));
        Assert.That(session.Clubs.ContainsKey(clubId), Is.False);
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetClub(playerLookup, clubId), Is.Null);
    }
}

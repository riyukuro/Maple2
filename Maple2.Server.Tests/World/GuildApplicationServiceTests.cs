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
using Maple2.Model.Validators;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using ChannelGuildRequest = Maple2.Server.Channel.Service.GuildRequest;
using ChannelGuildResponse = Maple2.Server.Channel.Service.GuildResponse;

namespace Maple2.Server.Tests.World;

// The World's side of a guild invite answer (Guild 3) and of the join requests (82 apply, 83 cancel, 84 answer),
// through WorldService.Guild as a channel calls it: the stored rows, the member who joins, and what the members
// with the invite right and the applicant are told.
public class GuildApplicationServiceTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChannelId = 1;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<ChannelGuildRequest> Guilds = [];
        public readonly List<MailNotificationRequest> Mails = [];

        public override ChannelGuildResponse Guild(ChannelGuildRequest request, CallOptions options) {
            Guilds.Add(request.Clone());
            return new ChannelGuildResponse();
        }

        public override MailNotificationResponse MailNotification(MailNotificationRequest request, CallOptions options) {
            Mails.Add(request.Clone());
            return new MailNotificationResponse();
        }
    }

    private TestGameSession test = null!;
    private FakeChannel channel = null!;
    private PlayerInfoLookup playerLookup = null!;
    private GuildLookup lookup = null!;
    private ChannelClientLookup channels = null!;
    private WorldService world = null!;
    private Guild guild = null!;
    private Guild other = null!;
    private long master;
    private long officer;
    private long applicant;
    private long plain;

    // A stored character of the test account (a guild member row needs one).
    private long CreateCharacter(string name) {
        using GameStorage.Request db = test.Storage.Context();
        long accountId = db.GetCharacter(test.CharacterId, -1)?.AccountId ?? 0;
        var model = new Character { AccountId = accountId, Name = name, Job = Job.Newbie, Level = 20, Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

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
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property with { Level = 1, Capacity = 10 } }));
        master = test.CharacterId;
        officer = CreateCharacter("Officer");
        applicant = CreateCharacter("Applicant");
        long otherMaster = CreateCharacter("Other");
        plain = CreateCharacter("Plain");

        channels = new ChannelClientLookup();
        channel = new FakeChannel();
        AddChannel(channels, ChannelId, channel);
        playerLookup = new PlayerInfoLookup(test.Storage, channels);
        var cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(playerLookup)!;
        cache[master] = Info(master, "Master");
        cache[officer] = Info(officer, "Officer");
        cache[applicant] = Info(applicant, "Applicant");
        cache[otherMaster] = Info(otherMaster, "Other");
        cache[plain] = Info(plain, "Plain");
        lookup = new GuildLookup(test.Storage, test.TableMetadata, test.ItemMetadata, test.ServerTableMetadata, channels, playerLookup);

        guild = AddGuild("Home", master, (officer, "Officer", 1));
        other = AddGuild("Away", otherMaster);
        world = new WorldService(null!, null!, channels, playerLookup, lookup, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!);
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
        playerLookup.Dispose();
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
    }

    // A stored guild held by the lookup; the master (rank 0, all rights) and members of the given ranks. Rank 1
    // gets the invite right here, as a master may give it.
    private Guild AddGuild(string name, long leaderId, params (long Id, string Name, byte Rank)[] members) {
        Guild created;
        using (GameStorage.Request db = test.Storage.Context()) {
            created = db.CreateGuild(name, leaderId) ?? throw new InvalidOperationException("guild row not created");
        }
        created.Members.Clear();
        created.Members[leaderId] = new GuildMember { GuildId = created.Id, Info = playerLookup.GetPlayerInfo(leaderId)!, Rank = 0, JoinTime = 1000 };
        foreach ((long id, string _, byte rank) in members) {
            created.Members[id] = new GuildMember { GuildId = created.Id, Info = playerLookup.GetPlayerInfo(id)!, Rank = rank, JoinTime = 1000 };
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.CreateGuildMember(created.Id, playerLookup.GetPlayerInfo(id)!), Is.Not.Null);
        }
        created.Ranks[1].Permission = GuildPermission.Default | GuildPermission.InviteMembers;
        var manager = new GuildManager(created) {
            GameStorage = test.Storage,
            ChannelClients = channels,
            TableMetadata = test.TableMetadata,
            ItemMetadata = test.ItemMetadata,
            ServerTableMetadata = test.ServerTableMetadata,
        };
        var guilds = (ConcurrentDictionary<long, GuildManager>) typeof(GuildLookup).GetField("guilds", Any)!.GetValue(lookup)!;
        guilds[created.Id] = manager;
        return created;
    }

    private GuildResponse Call(long requestorId, Action<GuildRequest> fill) {
        var request = new GuildRequest { RequestorId = requestorId };
        fill(request);
        channel.Guilds.Clear();
        return world.Guild(request, null!).GetAwaiter().GetResult();
    }

    private GuildResponse Apply(long guildId) =>
        Call(applicant, request => request.Apply = new GuildRequest.Types.Apply { GuildId = guildId });

    private IList<GuildApplicationRow> Open() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuildApplicationsOf(applicant);
    }

    private long StoredGuildOf(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuildId(characterId);
    }

    [Test]
    public void Apply_IsStoredAndTheInviteRightHoldersAreTold() {
        GuildResponse response = Apply(guild.Id);

        GuildApplicationRow row = Open().Single();
        Assert.Multiple(() => {
            Assert.That((response.Error, response.GuildId, response.ApplicationId, response.GuildName), Is.EqualTo((0, guild.Id, row.Id, "Home")));
            Assert.That(row.GuildId, Is.EqualTo(guild.Id));
            ChannelGuildRequest added = channel.Guilds.Single();
            Assert.That(added.ApplicationAdded.ApplicationId, Is.EqualTo(row.Id));
            Assert.That(added.ApplicationAdded.ApplicantId, Is.EqualTo(applicant));
            Assert.That(added.ReceiverIds, Is.EquivalentTo(new[] { master, officer }));
        });
    }

    // A second request to the same guild, a guild off the list, an applicant who has a guild, an unknown guild.
    [Test]
    public void Apply_Refusals_StoreNothing() {
        Assert.That(Apply(guild.Id).Error, Is.Zero);
        Assert.That(Apply(guild.Id).Error, Is.EqualTo((int) GuildError.s_guild_search_last_request));

        Assert.That(Call(officer, request => request.Apply = new GuildRequest.Types.Apply { GuildId = other.Id }).Error,
            Is.EqualTo((int) GuildError.s_guild_err_has_guild));
        Assert.That(Apply(999_999).Error, Is.EqualTo((int) GuildError.s_guild_err_null_guild));
        other.Listed = false;
        Assert.That(Apply(other.Id).Error, Is.EqualTo((int) GuildError.s_guild_err_null_guild));
        other.Listed = true;

        Assert.That(Open().Select(row => row.GuildId), Is.EqualTo(new[] { guild.Id }));
        Assert.That(channel.Guilds, Is.Empty);
    }

    // The client's id may be the guild's or the request's (GuildRules.FindOwnApplication); another's request is not found.
    [TestCase(true)]
    [TestCase(false)]
    public void Cancel_RemovesTheOwnRequest(bool byGuildId) {
        Apply(guild.Id);
        GuildApplicationRow row = Open().Single();

        GuildResponse response = Call(applicant, request => request.CancelApplication = new GuildRequest.Types.CancelApplication {
            Id = byGuildId ? guild.Id : row.Id,
        });

        Assert.That((response.Error, response.GuildId, response.ApplicationId, response.GuildName), Is.EqualTo((0, guild.Id, row.Id, "Home")));
        Assert.That(Open(), Is.Empty);
        Assert.That(channel.Guilds.Single().ApplicationRemoved.ApplicationId, Is.EqualTo(row.Id));
    }

    [Test]
    public void Cancel_AnothersRequest_IsNotFound() {
        Apply(guild.Id);
        GuildApplicationRow row = Open().Single();

        GuildResponse response = Call(officer, request => request.CancelApplication = new GuildRequest.Types.CancelApplication { Id = row.Id });

        Assert.That(response.Error, Is.EqualTo((int) GuildError.s_guild_search_null_join_guild_request));
        Assert.That(Open(), Has.Count.EqualTo(1));
    }

    // An officer with the invite right accepts: the applicant is a stored member, the request to the other guild is
    // gone and that guild's handlers are told; the officers hear the answer, the applicant the result.
    [Test]
    public void Respond_Accepted_TheApplicantJoins() {
        Apply(guild.Id);
        Apply(other.Id);
        GuildApplicationRow row = Open().Single(open => open.GuildId == guild.Id);
        GuildApplicationRow away = Open().Single(open => open.GuildId == other.Id);

        GuildResponse response = Call(officer, request => request.RespondApplication = new GuildRequest.Types.RespondApplication {
            GuildId = guild.Id, Id = row.Id, Accepted = true,
        });

        Assert.Multiple(() => {
            Assert.That((response.Error, response.ApplicationId, response.ApplicantName), Is.EqualTo((0, row.Id, "Applicant")));
            Assert.That(StoredGuildOf(applicant), Is.EqualTo(guild.Id));
            Assert.That(guild.Members.ContainsKey(applicant), Is.True);
            Assert.That(Open(), Is.Empty);
            Assert.That(channel.Guilds.Where(sent => sent.ApplicationRemoved != null).Select(sent => sent.ApplicationRemoved.ApplicationId),
                Is.EqualTo(new[] { away.Id }));
            ChannelGuildRequest answered = channel.Guilds.Single(sent => sent.ApplicationAnswered != null);
            Assert.That((answered.ApplicationAnswered.RequestorName, answered.ApplicationAnswered.Accepted), Is.EqualTo(("Officer", true)));
            Assert.That(answered.ReceiverIds, Does.Not.Contain(applicant));
            ChannelGuildRequest result = channel.Guilds.Single(sent => sent.ApplicationResult != null);
            Assert.That((result.ReceiverIds.Single(), result.ApplicationResult.Joined), Is.EqualTo((applicant, true)));
        });
    }

    // Rejected: the request is deleted and the applicant gets the reject mail.
    [Test]
    public void Respond_Rejected_DeletesAndMails() {
        Apply(guild.Id);
        GuildApplicationRow row = Open().Single();

        GuildResponse response = Call(master, request => request.RespondApplication = new GuildRequest.Types.RespondApplication {
            GuildId = guild.Id, Id = row.Id, Accepted = false,
        });

        Assert.That(response.Error, Is.Zero);
        Assert.That(Open(), Is.Empty);
        Assert.That(StoredGuildOf(applicant), Is.Zero);
        using GameStorage.Request db = test.Storage.Context();
        Mail mail = db.GetAllMail(applicant).Single();
        Assert.That(mail.Title, Is.EqualTo(GuildRules.RejectedApplicationMail(applicant, "Home").Title));
        Assert.That(channel.Guilds.Single(sent => sent.ApplicationResult != null).ApplicationResult.Joined, Is.False);
    }

    // The member of rank 2 has no invite right; a non-member, another guild's request id, an unknown guild.
    [Test]
    public void Respond_Refusals_KeepTheRequest() {
        guild.Members[plain] = new GuildMember { GuildId = guild.Id, Info = playerLookup.GetPlayerInfo(plain)!, Rank = 2, JoinTime = 1000 };
        Apply(guild.Id);
        Apply(other.Id);
        GuildApplicationRow row = Open().Single(open => open.GuildId == guild.Id);
        GuildApplicationRow away = Open().Single(open => open.GuildId == other.Id);

        GuildResponse Respond(long requestor, long guildId, long id) => Call(requestor, request => request.RespondApplication =
            new GuildRequest.Types.RespondApplication { GuildId = guildId, Id = id, Accepted = true });

        Assert.Multiple(() => {
            Assert.That(Respond(plain, guild.Id, row.Id).Error, Is.EqualTo((int) GuildError.s_guild_err_no_authority));
            Assert.That(Respond(applicant, guild.Id, row.Id).Error, Is.EqualTo((int) GuildError.s_guild_err_null_member));
            Assert.That(Respond(master, guild.Id, away.Id).Error, Is.EqualTo((int) GuildError.s_guild_search_null_join_guild_request));
            Assert.That(Respond(master, 999_999, row.Id).Error, Is.EqualTo((int) GuildError.s_guild_err_null_guild));
        });
        Assert.That(Open(), Has.Count.EqualTo(2));
        Assert.That(StoredGuildOf(applicant), Is.Zero);
    }

    // The applicant is a member of the other guild and the request is still there (a member insert removes the
    // requests in its own save, so here the row is written alone): the accept answers has_guild and the request
    // leaves the list.
    [Test]
    public void Respond_ApplicantInAGuildNow_RemovesTheRequest() {
        Apply(guild.Id);
        GuildApplicationRow row = Open().Single();
        using (var context = new Ms2Context(test.Options)) {
            context.GuildMember.Add(new Maple2.Database.Model.GuildMember {
                GuildId = other.Id, CharacterId = applicant, Rank = 2, CreationTime = DateTime.Now,
            });
            context.SaveChanges();
        }
        other.Members[applicant] = new GuildMember { GuildId = other.Id, Info = playerLookup.GetPlayerInfo(applicant)!, Rank = 2, JoinTime = 1000 };

        GuildResponse response = Call(master, request => request.RespondApplication = new GuildRequest.Types.RespondApplication {
            GuildId = guild.Id, Id = row.Id, Accepted = true,
        });

        Assert.That(response.Error, Is.EqualTo((int) GuildError.s_guild_err_has_guild));
        Assert.That(Open(), Is.Empty);
        Assert.That(channel.Guilds.Single().ApplicationRemoved.ApplicationId, Is.EqualTo(row.Id));
        Assert.That(guild.Members.ContainsKey(applicant), Is.False);
    }

    // Guild 3: the invited player's answer. Accepted: a stored member, and a request open to the other guild is
    // removed; rejected: the members hear reason RejectInvite; no open invite: null_invite_member.
    [Test]
    public void RespondInvite_Accepted_Joins() {
        GuildManager manager = Manager(guild);
        Assert.That(manager.Invite(master, playerLookup.GetPlayerInfo(applicant)!), Is.EqualTo(GuildError.none));
        Apply(other.Id);

        GuildResponse response = Call(applicant, request => request.RespondInvite = new GuildRequest.Types.RespondInvite {
            GuildId = guild.Id, Accepted = true,
        });

        Assert.That(response.Error, Is.Zero);
        Assert.That(response.Guild.Id, Is.EqualTo(guild.Id));
        Assert.That(StoredGuildOf(applicant), Is.EqualTo(guild.Id));
        Assert.That(Open(), Is.Empty);
        Assert.That(channel.Guilds.Count(sent => sent.ApplicationRemoved != null), Is.EqualTo(1));
        Assert.That(channel.Guilds.First(sent => sent.InviteReply != null).InviteReply.Reply, Is.EqualTo((int) GuildInvite.Response.Accept));
    }

    [Test]
    public void RespondInvite_RejectedOrNoInvite() {
        GuildManager manager = Manager(guild);
        Assert.That(manager.Invite(master, playerLookup.GetPlayerInfo(applicant)!), Is.EqualTo(GuildError.none));

        GuildResponse rejected = Call(applicant, request => request.RespondInvite = new GuildRequest.Types.RespondInvite {
            GuildId = guild.Id, Accepted = false,
        });
        Assert.That((rejected.Error, rejected.GuildId), Is.EqualTo((0, guild.Id)));
        Assert.That(channel.Guilds.Single().InviteReply.Reply, Is.EqualTo((int) GuildInvite.Response.RejectInvite));

        GuildResponse again = Call(applicant, request => request.RespondInvite = new GuildRequest.Types.RespondInvite {
            GuildId = guild.Id, Accepted = true,
        });
        Assert.That(again.Error, Is.EqualTo((int) GuildError.s_guild_err_null_invite_member));
        Assert.That(StoredGuildOf(applicant), Is.Zero);
    }

    private GuildManager Manager(Guild of) {
        Assert.That(lookup.TryGet(of.Id, out GuildManager? manager), Is.True);
        return manager!;
    }
}

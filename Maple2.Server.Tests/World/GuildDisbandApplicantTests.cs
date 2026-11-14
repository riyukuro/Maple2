using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Channel.Service;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using ChannelGuildRequest = Maple2.Server.Channel.Service.GuildRequest;
using ChannelGuildResponse = Maple2.Server.Channel.Service.GuildResponse;

namespace Maple2.Server.Tests.World;

// A guild's open join requests go with the guild when its master disbands it (GameStorage.DeleteGuild). Each
// applicant is answered as for a declined request: the reject mail, and Guild 48 while online so the request leaves
// their list.
public class GuildDisbandApplicantTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ChannelId = 1;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<MailNotificationRequest> Mails = [];
        public readonly List<ChannelGuildRequest> Guilds = [];

        public override MailNotificationResponse MailNotification(MailNotificationRequest request, CallOptions options) {
            Mails.Add(request.Clone());
            return new MailNotificationResponse();
        }

        public override ChannelGuildResponse Guild(ChannelGuildRequest request, CallOptions options) {
            Guilds.Add(request.Clone());
            return new ChannelGuildResponse();
        }
    }

    private TestGameSession test = null!;
    private FakeChannel channel = null!;
    private PlayerInfoLookup playerLookup = null!;
    private GuildLookup lookup = null!;
    private Guild guild = null!;
    private long online;
    private long offline;

    private static PlayerInfo Info(long characterId, string name, short channelId) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = channelId,
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
        // The guild delete goes by batch deletes, which need a context they can open on the test database.
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = _ => new Ms2Context(test.Options);
        online = test.CharacterId + 500;
        offline = test.CharacterId + 501;
        var channels = new ChannelClientLookup();
        channel = new FakeChannel();
        AddChannel(channels, ChannelId, channel);
        playerLookup = new PlayerInfoLookup(test.Storage, channels);
        var cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(playerLookup)!;
        cache[online] = Info(online, "Online", ChannelId);
        cache[offline] = Info(offline, "Offline", -1);
        lookup = new GuildLookup(test.Storage, test.TableMetadata, test.ItemMetadata, test.ServerTableMetadata, channels, playerLookup);

        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Gone", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        var master = new GuildMember {
            GuildId = guild.Id,
            Info = Info(test.CharacterId, "Master", ChannelId),
            Rank = 0,
            JoinTime = 1000,
        };
        guild.Members[master.CharacterId] = master;
        var manager = new GuildManager(guild) {
            GameStorage = test.Storage,
            ChannelClients = channels,
            TableMetadata = test.TableMetadata,
            ItemMetadata = test.ItemMetadata,
            ServerTableMetadata = test.ServerTableMetadata,
        };
        var guilds = (ConcurrentDictionary<long, GuildManager>) typeof(GuildLookup).GetField("guilds", Any)!.GetValue(lookup)!;
        guilds[guild.Id] = manager;
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
        playerLookup.Dispose();
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
    }

    private IList<Mail> MailOf(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(characterId).ToList();
    }

    [Test]
    public void Disband_AnswersEachApplicantAsDeclined() {
        GuildApplicationRow toOnline, toOffline;
        using (GameStorage.Request db = test.Storage.Context()) {
            toOnline = db.CreateGuildApplication(guild.Id, online)!;
            toOffline = db.CreateGuildApplication(guild.Id, offline)!;
        }

        Assert.That(lookup.Disband(test.CharacterId, guild.Id), Is.EqualTo(GuildError.none));

        Mail expected = GuildRules.RejectedApplicationMail(online, "Gone");
        foreach (long applicant in new[] { online, offline }) {
            IList<Mail> mails = MailOf(applicant);
            Assert.That(mails, Has.Count.EqualTo(1), $"mails of {applicant}");
            Assert.That(mails[0].Title, Is.EqualTo(expected.Title));
            Assert.That(mails[0].Content, Is.EqualTo(expected.Content));
        }
        Assert.That(channel.Mails.Select(mail => mail.CharacterId), Is.EqualTo(new[] { online }), "new-mail notice only while online");
        Assert.That(channel.Guilds, Has.Count.EqualTo(1));
        ChannelGuildRequest result = channel.Guilds[0];
        Assert.That(result.ReceiverIds, Is.EqualTo(new[] { online }));
        Assert.That(result.ApplicationResult.ApplicationId, Is.EqualTo(toOnline.Id));
        Assert.That(result.ApplicationResult.GuildName, Is.EqualTo("Gone"));
        Assert.That(result.ApplicationResult.Joined, Is.False);
        Assert.That(toOffline.Id, Is.Not.EqualTo(toOnline.Id));
    }

    [Test]
    public void DisbandRefused_SendsNothing() {
        using (GameStorage.Request db = test.Storage.Context()) {
            db.CreateGuildApplication(guild.Id, online);
        }

        Assert.That(lookup.Disband(test.CharacterId + 900, guild.Id), Is.EqualTo(GuildError.s_guild_err_no_master));

        Assert.That(MailOf(online), Is.Empty);
        Assert.That(channel.Mails, Is.Empty);
        Assert.That(channel.Guilds, Is.Empty);
    }
}

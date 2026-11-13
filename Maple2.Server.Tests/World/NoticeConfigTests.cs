using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Validators;
using Maple2.Server.World;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// The world's notice config (NoticeRules): a status answers both flags, on/off set them, a banner change or show goes
// to every channel, and a channel that becomes active gets the banner when it is on.
public class NoticeConfigTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<AdminRequest> Admins = [];

        public override AdminResponse Admin(AdminRequest request, CallOptions options) {
            Admins.Add(request);
            return new AdminResponse();
        }

        public override Maple2.Server.Channel.Service.ChannelsUpdateResponse UpdateChannels(
            Maple2.Server.Channel.Service.ChannelsUpdateRequest request, CallOptions options) => new();
    }

    private static object NewChannel(int id, ChannelClient client, string status) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, status), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
    }

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        var channels = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        channels[id] = NewChannel(id, client, "Active");
    }

    private static WorldServer World() {
        var world = (WorldServer) RuntimeHelpers.GetUninitializedObject(typeof(WorldServer));
        GC.SuppressFinalize(world);
        typeof(WorldServer).GetField("noticeMutex", Any)!.SetValue(world, new object());
        typeof(WorldServer).GetField("memoryStringBoards", Any)!.SetValue(world, new ConcurrentDictionary<int, string>());
        return world;
    }

    private FakeChannel first = null!;
    private FakeChannel second = null!;
    private WorldServer world = null!;
    private WorldService service = null!;

    [SetUp]
    public void SetUp() {
        var channels = new ChannelClientLookup();
        first = new FakeChannel();
        second = new FakeChannel();
        AddChannel(channels, 1, first);
        AddChannel(channels, 2, second);
        world = World();
        service = (WorldService) RuntimeHelpers.GetUninitializedObject(typeof(WorldService));
        typeof(WorldService).GetField("channelClients", Any)!.SetValue(service, channels);
        typeof(WorldService).GetField("worldServer", Any)!.SetValue(service, world);
        typeof(WorldService).GetField("logger", Any)!.SetValue(service, new LoggerConfiguration().CreateLogger());
    }

    private AdminResponse Notice(int kind, int action) {
        return service.Admin(new AdminRequest {
            RequesterId = 5,
            Notice = new AdminRequest.Types.Notice { Kind = kind, Action = action },
        }, null!).GetAwaiter().GetResult();
    }

    private static int[] Actions(FakeChannel channel) {
        int[] actions = channel.Admins.Where(request => request.RequestCase == AdminRequest.RequestOneofCase.Notice)
            .Select(request => request.Notice.Kind * 10 + request.Notice.Action).ToArray();
        channel.Admins.Clear();
        return actions;
    }

    [Test]
    public void OffUntilSet() {
        AdminResponse status = Notice(NoticeRules.Banner, NoticeRules.Status);
        Assert.Multiple(() => {
            Assert.That((status.NoticeBanner, status.NoticeLogin), Is.EqualTo((false, false)));
            Assert.That(status.Error, Is.EqualTo(0));
            Assert.That(Actions(first), Is.Empty); // a status asks no channel
        });
    }

    [Test]
    public void TheLoginNoticeStaysInTheWorld() {
        AdminResponse on = Notice(NoticeRules.Login, NoticeRules.On);
        Assert.That((on.NoticeBanner, on.NoticeLogin), Is.EqualTo((false, true)));
        Assert.That(world.NoticeConfig(), Is.EqualTo((false, true)));
        Assert.That(Actions(first).Concat(Actions(second)), Is.Empty);

        AdminResponse off = Notice(NoticeRules.Login, NoticeRules.Off);
        Assert.That(off.NoticeLogin, Is.False);
    }

    [Test]
    public void ABannerChangeGoesToEveryChannel() {
        AdminResponse on = Notice(NoticeRules.Banner, NoticeRules.On);
        Assert.That((on.NoticeBanner, on.NoticeLogin, on.Message), Is.EqualTo((true, false, "")));
        Assert.That(Actions(first), Is.EqualTo(new[] { NoticeRules.On }));
        Assert.That(Actions(second), Is.EqualTo(new[] { NoticeRules.On }));

        AdminResponse show = Notice(NoticeRules.Banner, NoticeRules.Show);
        Assert.That(show.NoticeBanner, Is.True);
        Assert.That(Actions(first), Is.EqualTo(new[] { NoticeRules.Show }));
        Assert.That(Actions(second), Is.EqualTo(new[] { NoticeRules.Show }));

        Notice(NoticeRules.Banner, NoticeRules.Off);
        Assert.That(world.NoticeConfig().banner, Is.False);
        Assert.That(Actions(second), Is.EqualTo(new[] { NoticeRules.Off }));
    }

    [TestCase(NoticeRules.Login, NoticeRules.Show)]
    [TestCase(2, NoticeRules.On)]
    [TestCase(NoticeRules.Banner, 9)]
    public void AnUnknownRequestIsAnError(int kind, int action) {
        Notice(NoticeRules.Login, NoticeRules.On);
        AdminResponse response = Notice(kind, action);
        Assert.Multiple(() => {
            Assert.That(response.Error, Is.EqualTo(1));
            Assert.That(response.Message, Does.Contain($"kind {kind}, action {action}"));
            Assert.That((response.NoticeBanner, response.NoticeLogin), Is.EqualTo((false, true)));
            Assert.That(world.NoticeConfig(), Is.EqualTo((false, true)));
            Assert.That(Actions(first), Is.Empty);
        });
    }

    // ChannelClientLookup.Active: the replay to a channel that (re)starts.
    [TestCase(true)]
    [TestCase(false)]
    public void ANewChannelGetsTheBannerWhenOn(bool on) {
        if (on) {
            world.ApplyNotice(NoticeRules.Banner, NoticeRules.On);
        }
        var lookup = new ChannelClientLookup();
        typeof(ChannelClientLookup).GetField("worldServer", Any)!.SetValue(lookup, world);
        var fresh = new FakeChannel();
        object channel = NewChannel(3, fresh, "Active");
        ((System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!)[3] = channel;

        typeof(ChannelClientLookup).GetMethod("Active", Any)!.Invoke(lookup, [channel]);

        Assert.That(Actions(fresh), Is.EqualTo(on ? new[] { NoticeRules.On } : Array.Empty<int>()));
    }
}

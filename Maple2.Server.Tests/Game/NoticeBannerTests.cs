using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Web.Controllers;
using Maple2.Server.World.Service;
using Maple2.Tools.Scheduler;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game;

// The channel's side of the notice banner (NoticeRules): on and off set the channel's flag, a show sends
// NoticeDialog 0xCF mode 0 to every player in a field.
public class NoticeBannerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void TheBannerPacketIsModeZero() {
        Assert.That(Convert.ToHexString(NoticeDialogPacket.Show().ToArray()), Is.EqualTo("CF00" + "00"));
    }

    private static (GameServer server, ChannelService channel, TestGameSession[] tests) Channel() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        TestGameSession[] tests = [new(options), new(options)];
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        foreach (TestGameSession test in tests) {
            GameSession session = test.Start();
            test.TakeSent();
            sessions[session.CharacterId] = session;
        }
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        return (server, new ChannelService(server, null!, tests[0].Storage, null!, null!, null!), tests);
    }

    private static AdminResponse Notice(ChannelService channel, int kind, int action) {
        return channel.Admin(new AdminRequest {
            Notice = new AdminRequest.Types.Notice { Kind = kind, Action = action },
        }, null!).GetAwaiter().GetResult();
    }

    [Test]
    public void OnAndOffSetTheChannelsFlag() {
        (GameServer server, ChannelService channel, TestGameSession[] tests) = Channel();

        Notice(channel, NoticeRules.Banner, NoticeRules.On);
        Assert.That(server.NoticeBanner, Is.True);
        Notice(channel, NoticeRules.Login, NoticeRules.Off); // the login notice is the login server's
        Assert.That(server.NoticeBanner, Is.True);
        Notice(channel, NoticeRules.Banner, NoticeRules.Off);
        Assert.That(server.NoticeBanner, Is.False);
        Assert.That(tests.SelectMany(test => test.TakeSentOps()), Has.No.Member(SendOp.NoticeDialog));
    }

    [Test]
    public void AShowGoesToEveryPlayerInAField() {
        (GameServer server, ChannelService channel, TestGameSession[] tests) = Channel();

        Notice(channel, NoticeRules.Banner, NoticeRules.Show);

        foreach (TestGameSession test in tests) {
            string[] sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NoticeDialog)
                .Select(Convert.ToHexString).ToArray();
            Assert.That(sent, Is.EqualTo(new[] { "CF0000" }));
        }
        Assert.That(server.NoticeBanner, Is.False); // a show leaves the flag
    }

    // EnterField: the banner once per channel session while the channel has it on, NoticeRules.BannerDelay after the
    // entry (the client opens its notice dialog a few ticks after the entry and drops a 0xCF that comes before).
    [Test]
    public void TheBannerComesOnceAfterFieldEntry() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        var server = (GameServer) typeof(GameSession).GetField("server", Any)!.GetValue(session)!;
        typeof(GameSession).GetField(nameof(GameSession.Scheduler))!.SetValue(session, new EventQueue(Serilog.Log.Logger));
        int queued = session.Scheduler.Count;

        session.SendNoticeBanner();
        Assert.That(session.Scheduler.Count, Is.EqualTo(queued)); // off

        server.NoticeBanner = true;
        session.SendNoticeBanner();
        session.SendNoticeBanner();
        Assert.Multiple(() => {
            Assert.That(session.Scheduler.Count, Is.EqualTo(queued + 1));
            Assert.That(test.TakeSentOps(), Has.No.Member(SendOp.NoticeDialog)); // not in the entry burst
        });

        session.ShowNoticeBanner(); // the scheduled send
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.NoticeDialog)
            .Select(Convert.ToHexString), Is.EqualTo(new[] { "CF0000" }));
    }

    [Test]
    public void TheDelayCoversTheDialogOpening() {
        Assert.That(NoticeRules.BannerDelay, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    // OnServerEnter ends with the maview url, the web api url and the allowed url patterns; the banner page is
    // "maview:/Game/Banner", so the maview url is the web server's (no path, no trailing slash).
    [Test]
    public void ServerEnterGivesTheWebServerAsTheMaviewUrl() {
        Assert.Multiple(() => {
            Assert.That(ServerEnterPacket.MaviewUrl(new Uri("http://127.0.0.1:4600")), Is.EqualTo("http://127.0.0.1:4600"));
            Assert.That(ServerEnterPacket.MaviewUrl(new Uri("http://localhost:4000/")), Is.EqualTo("http://localhost:4000"));
            Assert.That(ServerEnterPacket.AllowedUrls(new Uri("http://127.0.0.1:4600")), Is.EqualTo("^https?://127\\.0\\.0\\.1(:\\d+)?"));
            Assert.That(ServerEnterPacket.AllowedUrls(new Uri("http://localhost:4000")), Is.EqualTo("^https?://localhost(:\\d+)?"));
        });

        var test = new TestGameSession();
        GameSession session = test.Start();
        byte[] packet = ServerEnterPacket.Request(session.Player).ToArray();
        string maview = ServerEnterPacket.MaviewUrl(Target.WebUri), allowed = ServerEnterPacket.AllowedUrls(Target.WebUri);
        string tail = Convert.ToHexString(Str(maview)) + "0000" + Convert.ToHexString(Str(allowed));
        Assert.That(Convert.ToHexString(packet), Does.EndWith(tail));
    }

    private static byte[] Str(string value) {
        byte[] text = System.Text.Encoding.Unicode.GetBytes(value);
        return BitConverter.GetBytes((short) value.Length).Concat(text).ToArray();
    }

    // The web server's banner page when the server has no Data/game/banner.html.
    [Test]
    public void TheBannerPageSaysThereIsNoNotice() {
        var page = new GameController().GetBanner() as ContentHttpResult;
        Assert.That(page, Is.Not.Null);
        Assert.That((page!.ResponseContent, page.ContentType),
            Is.EqualTo((GameController.DefaultBanner, "text/html; charset=utf-8")));
        Assert.That(GameController.DefaultBanner, Does.Contain("There is no notice."));
    }
}

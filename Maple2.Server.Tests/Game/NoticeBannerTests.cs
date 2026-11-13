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
using Maple2.Server.World.Service;
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

    // EnterField: the banner once per channel session while the channel has it on.
    [Test]
    public void TheBannerComesOnceAtFieldEntry() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        var server = (GameServer) typeof(GameSession).GetField("server", Any)!.GetValue(session)!;

        session.SendNoticeBanner();
        Assert.That(test.TakeSentOps(), Has.No.Member(SendOp.NoticeDialog)); // off

        server.NoticeBanner = true;
        session.SendNoticeBanner();
        session.SendNoticeBanner();
        Assert.That(test.TakeSentOps().Count(op => op == SendOp.NoticeDialog), Is.EqualTo(1));
    }
}

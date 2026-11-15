using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game;

// The Game server's World calls end by a deadline (WorldCallDeadline), and the notices nobody reads the answer of are
// not waited for (WorldNotices): a World that hangs does not hold a field's thread, and through it the field.
[NonParallelizable]
public class WorldCallDeadlineTests {
    private TimeSpan field, probe, other;

    [SetUp]
    public void Keep() {
        (field, probe, other) = (WorldCallDeadline.FieldWait, WorldCallDeadline.ProbeWait, WorldCallDeadline.OtherWait);
        WorldCallDeadline.WorldSlow = false;
        WorldCallDeadline.FieldThread = false;
    }

    [TearDown]
    public void Restore() {
        (WorldCallDeadline.FieldWait, WorldCallDeadline.ProbeWait, WorldCallDeadline.OtherWait) = (field, probe, other);
        WorldCallDeadline.WorldSlow = false;
        WorldCallDeadline.FieldThread = false;
    }

    // A World that takes the connection and never answers (a hung process; the call's HTTP/2 preface is not answered).
    private sealed class SilentWorld : IDisposable {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        public int Port => ((IPEndPoint) listener.LocalEndpoint).Port;

        public SilentWorld() {
            listener.Start();
            _ = Task.Run(async () => {
                var held = new System.Collections.Generic.List<TcpClient>();
                try {
                    while (!stop.IsCancellationRequested) {
                        held.Add(await listener.AcceptTcpClientAsync(stop.Token));
                    }
                } catch (OperationCanceledException) { } catch (SocketException) { } catch (ObjectDisposedException) { }
                foreach (TcpClient client in held) {
                    client.Dispose();
                }
            });
        }

        public void Dispose() {
            stop.Cancel();
            listener.Stop();
        }
    }

    // The World client the Game server registers (Program.cs), here pointed at the silent World.
    private static WorldClient GameWorld(int port) {
        var services = new ServiceCollection();
        new GameWorldClientModule().Configure(services);
        services.Configure<Grpc.Net.ClientFactory.GrpcClientFactoryOptions>(typeof(WorldClient).Name, options => options.Address = new Uri($"http://127.0.0.1:{port}"));
        return services.BuildServiceProvider().GetRequiredService<WorldClient>();
    }

    [Test]
    public void AFieldThreadsCall_ToAHungWorld_EndsByItsDeadline() {
        WorldCallDeadline.FieldWait = TimeSpan.FromMilliseconds(300);
        using var world = new SilentWorld();
        WorldClient client = GameWorld(world.Port);

        Task<StatusCode> call = Task.Factory.StartNew(() => {
            WorldCallDeadline.FieldThread = true;
            try {
                client.Guild(new GuildRequest { RequestorId = 1 });
                return StatusCode.OK;
            } catch (RpcException ex) {
                return ex.StatusCode;
            }
        }, TaskCreationOptions.LongRunning);

        Assert.That(call.Wait(TimeSpan.FromSeconds(5)), Is.True, "the call still waits on the World");
        Assert.Multiple(() => {
            Assert.That(call.Result, Is.EqualTo(StatusCode.DeadlineExceeded));
            Assert.That(WorldCallDeadline.WorldSlow, Is.True);
        });
    }

    // The deadlines picked: the call's own is kept, Disconnect gets none, a field's thread gets the short one, and the
    // probe one while the World did not answer there.
    [Test]
    public void TheDeadline_ByThreadAndMethod() {
        DateTime now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        DateTime own = now.AddMinutes(1);
        Assert.Multiple(() => {
            Assert.That(WorldCallDeadline.DeadlineFor("Guild", new CallOptions(deadline: own), now), Is.Null);
            Assert.That(WorldCallDeadline.DeadlineFor("Guild", new CallOptions(), now), Is.EqualTo(now + WorldCallDeadline.OtherWait));
            Assert.That(WorldCallDeadline.DeadlineFor("Disconnect", new CallOptions(), now), Is.Null);
        });

        WorldCallDeadline.FieldThread = true;
        Assert.Multiple(() => {
            Assert.That(WorldCallDeadline.DeadlineFor("Guild", new CallOptions(), now), Is.EqualTo(now + WorldCallDeadline.FieldWait));
            Assert.That(WorldCallDeadline.DeadlineFor("Disconnect", new CallOptions(), now), Is.Null);
        });
        WorldCallDeadline.WorldSlow = true;
        Assert.That(WorldCallDeadline.DeadlineFor("Guild", new CallOptions(), now), Is.EqualTo(now + WorldCallDeadline.ProbeWait));
    }

    private static readonly Method<GuildRequest, GuildResponse> GuildMethod = new(MethodType.Unary, "maple2.server.world.service.World", "Guild",
        Marshallers.Create<GuildRequest>(_ => [], _ => new GuildRequest()), Marshallers.Create<GuildResponse>(_ => [], _ => new GuildResponse()));

    private static GuildResponse Call(Func<CallOptions, GuildResponse> answer) {
        var context = new ClientInterceptorContext<GuildRequest, GuildResponse>(GuildMethod, null, new CallOptions());
        return WorldCallDeadline.Instance.BlockingUnaryCall(new GuildRequest(), context, (_, ctx) => answer(ctx.Options));
    }

    // One call that ran out on a field's thread makes the next ones there short; one answered call ends that.
    [Test]
    public void AnAnsweredCall_EndsTheShortWaits() {
        WorldCallDeadline.FieldThread = true;
        Assert.Throws<RpcException>(() => Call(_ => throw new RpcException(new Status(StatusCode.DeadlineExceeded, "late"))));
        Assert.That(WorldCallDeadline.WorldSlow, Is.True);

        DateTime? deadline = null;
        Call(options => {
            deadline = options.Deadline;
            return new GuildResponse();
        });
        Assert.Multiple(() => {
            Assert.That(deadline, Is.LessThanOrEqualTo(DateTime.UtcNow + WorldCallDeadline.ProbeWait));
            Assert.That(WorldCallDeadline.WorldSlow, Is.False);
        });
    }

    // A mail sent while the World hangs: the handler (on the field's thread) ends at once; the notice is sent.
    [Test]
    public void AMailNotice_ToAHungWorld_DoesNotHoldTheHandler() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var sender = new TestGameSession(options);
        var recipient = new TestGameSession(options);
        foreach (TestGameSession test in new[] { sender, recipient }) {
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
            test.TakeSent();
        }
        using var hang = new ManualResetEventSlim(false);
        sender.World.MailNoticeHang = hang;
        var handler = new MailHandler { BanWordStorage = TestGameSession.BanWords() };
        GameSession session = sender.Session;
        string name = recipient.Session.PlayerName;

        Task send = Task.Run(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteUnicodeString(name);
            writer.WriteUnicodeString("hello");
            writer.WriteUnicodeString("text");
        })));
        bool ended = send.Wait(TimeSpan.FromSeconds(2));
        hang.Set();
        send.Wait(TimeSpan.FromSeconds(5));

        Assert.Multiple(() => {
            Assert.That(ended, Is.True, "the handler waited for the World's answer to the notice");
            Assert.That(sender.World.MailNotifications.Select(notice => notice.CharacterId), Is.EqualTo(new[] { recipient.Session.CharacterId }));
        });
    }
}

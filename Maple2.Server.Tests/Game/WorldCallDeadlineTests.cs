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
    private TimeSpan field, retry, other;

    [SetUp]
    public void Keep() {
        (field, retry, other) = (WorldCallDeadline.FieldWait, WorldCallDeadline.RetryWait, WorldCallDeadline.OtherWait);
        WorldCallDeadline.Reset();
        WorldCallDeadline.FieldThread = false;
    }

    [TearDown]
    public void Restore() {
        (WorldCallDeadline.FieldWait, WorldCallDeadline.RetryWait, WorldCallDeadline.OtherWait) = (field, retry, other);
        WorldCallDeadline.Reset();
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

    // The deadlines picked: off a field's thread the call's own is kept, else OtherWait; on a field's thread at most
    // FieldWait (a longer own one, as the channel moves' 10 s, is cut); Disconnect is left alone.
    [Test]
    public void TheDeadline_ByThreadAndMethod() {
        DateTime now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        DateTime longer = now.AddSeconds(10);
        DateTime shorter = now.AddSeconds(2);
        Assert.Multiple(() => {
            Assert.That(WorldCallDeadline.DeadlineFor("Guild", new CallOptions(deadline: longer), now), Is.Null);
            Assert.That(WorldCallDeadline.DeadlineFor("Guild", new CallOptions(), now), Is.EqualTo(now + WorldCallDeadline.OtherWait));
            Assert.That(WorldCallDeadline.DeadlineFor("Disconnect", new CallOptions(), now), Is.Null);
        });

        WorldCallDeadline.FieldThread = true;
        Assert.Multiple(() => {
            Assert.That(WorldCallDeadline.DeadlineFor("Guild", new CallOptions(), now), Is.EqualTo(now + WorldCallDeadline.FieldWait));
            Assert.That(WorldCallDeadline.DeadlineFor("MigrateOut", new CallOptions(deadline: longer), now), Is.EqualTo(now + WorldCallDeadline.FieldWait));
            Assert.That(WorldCallDeadline.DeadlineFor("MapPopulation", new CallOptions(deadline: shorter), now), Is.Null);
            Assert.That(WorldCallDeadline.DeadlineFor("Disconnect", new CallOptions(), now), Is.Null);
        });
    }

    private static readonly Method<GuildRequest, GuildResponse> GuildMethod = new(MethodType.Unary, "maple2.server.world.service.World", "Guild",
        Marshallers.Create<GuildRequest>(_ => [], _ => new GuildRequest()), Marshallers.Create<GuildResponse>(_ => [], _ => new GuildResponse()));

    private static GuildResponse Call(Func<CallOptions, GuildResponse> answer, CallOptions? options = null) {
        var context = new ClientInterceptorContext<GuildRequest, GuildResponse>(GuildMethod, null, options ?? new CallOptions());
        return WorldCallDeadline.Instance.BlockingUnaryCall(new GuildRequest(), context, (_, ctx) => answer(ctx.Options));
    }

    private static GuildResponse Late(CallOptions _) => throw new RpcException(new Status(StatusCode.DeadlineExceeded, "late"));

    // After a call on a field's thread ran out, the next ones there are not sent (the World cannot apply what the
    // caller then refunds) and fail as for a World that is down, a call with its own deadline as well.
    [Test]
    public void AfterAMiss_FieldCallsAreNotSent() {
        WorldCallDeadline.FieldThread = true;
        Assert.Throws<RpcException>(() => Call(Late));
        Assert.That(WorldCallDeadline.WorldSlow, Is.True);

        int sent = 0;
        var plain = Assert.Throws<RpcException>(() => Call(_ => {
            sent++;
            return new GuildResponse();
        }));
        var own = Assert.Throws<RpcException>(() => Call(_ => {
            sent++;
            return new GuildResponse();
        }, new CallOptions(deadline: DateTime.UtcNow.AddSeconds(10))));
        Assert.Multiple(() => {
            Assert.That(sent, Is.Zero);
            Assert.That(plain!.StatusCode, Is.EqualTo(StatusCode.Unavailable));
            Assert.That(own!.StatusCode, Is.EqualTo(StatusCode.Unavailable));
            Assert.That(WorldCallDeadline.WorldSlow, Is.True);
        });

        // Off a field's thread calls are still sent.
        WorldCallDeadline.FieldThread = false;
        Call(_ => {
            sent++;
            return new GuildResponse();
        });
        Assert.That(sent, Is.EqualTo(1));
    }

    // When the retry time came one call is sent with the full field wait (a second one meanwhile is not), and its
    // answer ends the state.
    [Test]
    public void AtTheRetryTime_OneCallIsSent_AndItsAnswerEndsTheState() {
        WorldCallDeadline.FieldThread = true;
        WorldCallDeadline.SetSlow(DateTime.UtcNow.AddSeconds(-1));

        DateTime? deadline = null;
        bool othersAdmitted = true;
        Call(options => {
            deadline = options.Deadline;
            othersAdmitted = WorldCallDeadline.Admit("Guild", DateTime.UtcNow);
            return new GuildResponse();
        });
        Assert.Multiple(() => {
            Assert.That(deadline, Is.GreaterThan(DateTime.UtcNow + WorldCallDeadline.FieldWait - TimeSpan.FromSeconds(1)));
            Assert.That(othersAdmitted, Is.False);
            Assert.That(WorldCallDeadline.WorldSlow, Is.False);
            Assert.That(WorldCallDeadline.Admit("Guild", DateTime.UtcNow), Is.True);
        });

        // A retry that runs out again keeps the state.
        WorldCallDeadline.SetSlow(DateTime.UtcNow.AddSeconds(-1));
        Assert.Throws<RpcException>(() => Call(Late));
        Assert.That(WorldCallDeadline.Admit("Guild", DateTime.UtcNow), Is.False);
    }

    // An answer to a call off a field's thread (a session's load, a call with its own deadline) ends the state too:
    // a World that answers, if not fast, is not skipped on.
    [Test]
    public void AnAnswerOffTheFieldThread_EndsTheState() {
        WorldCallDeadline.SetSlow(DateTime.UtcNow.AddMinutes(1));
        Call(_ => new GuildResponse(), new CallOptions(deadline: DateTime.UtcNow.AddSeconds(10)));
        Assert.That(WorldCallDeadline.WorldSlow, Is.False);
    }

    // A call's own shorter deadline (MapPopulation's 2 s) that runs out does not make the World count as not
    // answering: only one on the FieldWait cap does. Before, every field call failed for RetryWait after it.
    [Test]
    public void AnOwnShorterDeadline_ThatRunsOut_SkipsNothing() {
        WorldCallDeadline.FieldThread = true;
        Assert.Throws<RpcException>(() => Call(Late, new CallOptions(deadline: DateTime.UtcNow.AddSeconds(2))));
        Assert.That(WorldCallDeadline.WorldSlow, Is.False);

        Assert.Throws<RpcException>(() => Call(Late));
        Assert.That(WorldCallDeadline.WorldSlow, Is.True);
    }

    // A World client whose calls go through the interceptor and are answered at once (counted).
    private sealed class CountingInvoker : CallInvoker {
        public int Calls;

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) {
            Calls++;
            return (TResponse) Activator.CreateInstance(typeof(TResponse))!;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private static GameSession SessionWith(WorldClient world) {
        var session = (GameSession) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        typeof(GameSession).GetProperty(nameof(GameSession.World))!.SetValue(session, world);
        typeof(Maple2.Server.Core.Network.Session).GetField("Logger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session, new Serilog.LoggerConfiguration().CreateLogger());
        return session;
    }

    private static object? Invoke(GameSession session, string name, object[] args) {
        System.Reflection.MethodInfo method = typeof(GameSession).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return method.Invoke(session, args);
    }

    // While the World is not answering, the account lock is not asked on a field's thread: that is a lock held (the
    // store is refused), not a World that is down (which holds none, and the store went on without the lock). The
    // release of a lock taken is still sent, else the lock stayed held to its expiry.
    [Test]
    public void WhileTheWorldIsNotAnswering_TheLockCountsAsHeld_AndItsReleaseIsSent() {
        var invoker = new CountingInvoker();
        GameSession session = SessionWith(new WorldClient(invoker.Intercept(WorldCallDeadline.Instance)));
        WorldCallDeadline.FieldThread = true;
        WorldCallDeadline.SetSlow(DateTime.UtcNow.AddMinutes(1));

        object? take = Invoke(session, "AcquireLock", [1L, 0L, 5]);
        Assert.Multiple(() => {
            Assert.That(take?.ToString(), Is.EqualTo("Held"));
            Assert.That(invoker.Calls, Is.Zero);
        });

        Invoke(session, "ReleaseLock", [1L, 7L]);
        Assert.That(invoker.Calls, Is.EqualTo(1));
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

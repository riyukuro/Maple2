using Grpc.Core;
using Grpc.Core.Interceptors;
using Maple2.Server.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Maple2.Server.Game.Util.Sync;

// The deadline of a World call. Without one a call waits until the World answers: a World that hangs (stuck on its
// database, on a game server it calls in turn, or a host gone from the network) held the caller as long, and on a
// field's thread (its packets, the commands, the triggers, the ticks) every player of that field.
// On a field's thread a call gets at most FieldWait (a call's own longer deadline is cut to it). After one such call
// ran out the World counts as not answering: the next calls on a field's thread are not sent but fail at once
// (Unavailable, as for a World that is down), so a player whose packets each ask the World cannot hold the field
// FieldWait per packet. A call not sent cannot be applied by the World later, so the callers' refunds on a failure
// stay right. Every RetryWait one call is let through with the full FieldWait; any answered call (on any thread)
// ends the state. Elsewhere (the receive threads, the sessions' loads and closes, the World's own calls to this
// server) a call without a deadline gets OtherWait. Disconnect is left alone: the World answers it once the kicked
// session's close ended (GameSession.Kick asks it off the field thread).
public sealed class WorldCallDeadline : Interceptor {
    public static readonly WorldCallDeadline Instance = new();

    internal static TimeSpan FieldWait = TimeSpan.FromSeconds(5);
    internal static TimeSpan RetryWait = TimeSpan.FromSeconds(5);
    internal static TimeSpan OtherWait = TimeSpan.FromSeconds(10);

    // The calls left as they are (method names of the World service).
    private static readonly HashSet<string> Unbounded = ["Disconnect"];

    // The thread is a field's loop (FieldManager.UpdateLoop).
    [ThreadStatic]
    private static bool fieldThread;
    public static void MarkFieldThread() => fieldThread = true;
    internal static bool FieldThread {
        get => fieldThread;
        set => fieldThread = value;
    }

    // A call on a field's thread ran out and none was answered since.
    private static volatile bool worldSlow;
    // While the World is not answering: the time (UTC ticks) the next call on a field's thread may be sent.
    private static long retryAt;
    internal static bool WorldSlow => worldSlow;

    internal static void SetSlow(DateTime retry) {
        Interlocked.Exchange(ref retryAt, retry.Ticks);
        worldSlow = true;
    }

    internal static void Reset() {
        worldSlow = false;
        Interlocked.Exchange(ref retryAt, 0);
    }

    private static readonly ILogger Logger = Log.Logger.ForContext<WorldCallDeadline>();

    private WorldCallDeadline() { }

    // The deadline the call gets: null when it keeps its own (or none).
    internal static DateTime? DeadlineFor(string method, CallOptions options, DateTime now) {
        if (Unbounded.Contains(method)) {
            return null;
        }
        if (!fieldThread) {
            return options.Deadline == null ? now + OtherWait : null;
        }
        DateTime cap = now + FieldWait;
        return options.Deadline == null || options.Deadline > cap ? cap : null;
    }

    // A call on a field's thread is sent: the World answers, or the retry time came (this call takes it).
    internal static bool Admit(string method, DateTime now) {
        if (!fieldThread || !worldSlow || Unbounded.Contains(method)) {
            return true;
        }
        long retry = Interlocked.Read(ref retryAt);
        if (now.Ticks < retry) {
            return false;
        }
        // The other fields' calls stay skipped while this one is out.
        return Interlocked.CompareExchange(ref retryAt, (now + FieldWait + RetryWait).Ticks, retry) == retry;
    }

    private static ClientInterceptorContext<TRequest, TResponse> WithDeadline<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class where TResponse : class {
        DateTime? deadline = DeadlineFor(context.Method.Name, context.Options, DateTime.UtcNow);
        if (deadline == null) {
            return context;
        }
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithDeadline(deadline.Value));
    }

    private static void Answered(string method, Exception? error) {
        if (error is RpcException { StatusCode: StatusCode.DeadlineExceeded }) {
            if (!fieldThread) {
                return;
            }
            if (!worldSlow) {
                Logger.Warning("World call {Method} on a field thread ran out: calls there are not sent for {Retry} s, or until one is answered", method, RetryWait.TotalSeconds);
            }
            SetSlow(DateTime.UtcNow + RetryWait);
        } else if (error is null or RpcException { StatusCode: not StatusCode.Unavailable }) {
            if (worldSlow) {
                Logger.Information("World answers again ({Method})", method);
            }
            worldSlow = false;
        }
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation) {
        if (Unbounded.Contains(context.Method.Name)) {
            return continuation(request, context);
        }
        if (!Admit(context.Method.Name, DateTime.UtcNow)) {
            throw new RpcException(new Status(StatusCode.Unavailable, $"World not answering: {context.Method.Name} not sent"));
        }
        context = WithDeadline(context);
        try {
            TResponse response = continuation(request, context);
            Answered(context.Method.Name, null);
            return response;
        } catch (Exception ex) {
            Answered(context.Method.Name, ex);
            throw;
        }
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation) {
        return continuation(request, WithDeadline(context));
    }
}

// The Game server's World client: the calls get WorldCallDeadline's deadlines.
public sealed class GameWorldClientModule : WorldClientModule {
    protected override void ConfigureWorld(IHttpClientBuilder world) {
        world.AddInterceptor(() => WorldCallDeadline.Instance);
    }
}

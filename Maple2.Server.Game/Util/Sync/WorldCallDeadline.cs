using Grpc.Core;
using Grpc.Core.Interceptors;
using Maple2.Server.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Maple2.Server.Game.Util.Sync;

// The deadline of a World call that sets none. Without one a call waits until the World answers: a World that hangs
// (stuck on its database, on a game server it calls in turn, or a host gone from the network) held the caller as
// long, and on a field's thread (its packets, the commands, the triggers, the ticks) every player of that field.
// On a field's thread the call gets FieldWait; after one such call ran out, the next ones there get ProbeWait (a
// World that answers does so in milliseconds) until a call is answered again, so a player whose packets each ask the
// World cannot hold the field FieldWait per packet while the World is down. Elsewhere (the receive threads, the
// sessions' loads and closes, the World's own calls to this server) a call gets OtherWait. Disconnect sets none:
// the World answers it once the kicked session's close ended (GameSession.Kick asks it off the field thread).
public sealed class WorldCallDeadline : Interceptor {
    public static readonly WorldCallDeadline Instance = new();

    internal static TimeSpan FieldWait = TimeSpan.FromSeconds(5);
    internal static TimeSpan ProbeWait = TimeSpan.FromMilliseconds(250);
    internal static TimeSpan OtherWait = TimeSpan.FromSeconds(10);

    // The calls left without a deadline (method names of the World service).
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
    internal static bool WorldSlow {
        get => worldSlow;
        set => worldSlow = value;
    }

    private static readonly ILogger Logger = Log.Logger.ForContext<WorldCallDeadline>();

    private WorldCallDeadline() { }

    // The deadline the call gets: null when it keeps its own (or none).
    internal static DateTime? DeadlineFor(string method, CallOptions options, DateTime now) {
        if (options.Deadline != null || Unbounded.Contains(method)) {
            return null;
        }
        if (!fieldThread) {
            return now + OtherWait;
        }
        return now + (worldSlow ? ProbeWait : FieldWait);
    }

    private static ClientInterceptorContext<TRequest, TResponse> WithDeadline<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, out bool timed)
        where TRequest : class where TResponse : class {
        DateTime? deadline = DeadlineFor(context.Method.Name, context.Options, DateTime.UtcNow);
        timed = deadline != null && fieldThread;
        if (deadline == null) {
            return context;
        }
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithDeadline(deadline.Value));
    }

    private static void Answered(string method, Exception? error) {
        if (error is RpcException { StatusCode: StatusCode.DeadlineExceeded }) {
            if (!worldSlow) {
                Logger.Warning("World call {Method} on a field thread ran out: the next ones there wait {Probe} ms until one is answered", method, ProbeWait.TotalMilliseconds);
            }
            worldSlow = true;
        } else if (error is null or RpcException { StatusCode: not StatusCode.Unavailable }) {
            worldSlow = false;
        }
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation) {
        context = WithDeadline(context, out bool timed);
        if (!timed) {
            return continuation(request, context);
        }
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
        return continuation(request, WithDeadline(context, out _));
    }
}

// The Game server's World client: the calls get WorldCallDeadline's deadlines.
public sealed class GameWorldClientModule : WorldClientModule {
    protected override void ConfigureWorld(IHttpClientBuilder world) {
        world.AddInterceptor(() => WorldCallDeadline.Instance);
    }
}

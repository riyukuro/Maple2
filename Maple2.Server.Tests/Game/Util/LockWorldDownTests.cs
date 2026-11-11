using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game.Util;

// A game server stopped after its world: the session saves at the stop, and the lock call before the save finds the
// world gone (gRPC Unavailable). The save goes on without the lock instead of being skipped.
public class LockWorldDownTests {
    private sealed class DownWorld(StatusCode status) : WorldClient {
        public int Calls;

        public override LockResponse AcquireLock(LockRequest request, CallOptions options) {
            Calls++;
            throw new RpcException(new Status(status, "Error connecting to subchannel."));
        }
    }

    private static GameSession SessionWith(WorldClient world) {
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        typeof(GameSession).GetProperty(nameof(GameSession.World))!.SetValue(session, world);
        typeof(Maple2.Server.Core.Network.Session).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, new LoggerConfiguration().CreateLogger());
        return session;
    }

    private static void AcquireLock(GameSession session) {
        MethodInfo acquire = typeof(GameSession).GetMethod("AcquireLock", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try {
            acquire.Invoke(session, [1L, 5]);
        } catch (TargetInvocationException ex) {
            throw ex.InnerException!;
        }
    }

    [Test]
    public void WorldDown_ReturnsAtOnceWithoutThrowing() {
        var world = new DownWorld(StatusCode.Unavailable);
        Assert.DoesNotThrow(() => AcquireLock(SessionWith(world)));
        Assert.That(world.Calls, Is.EqualTo(1));
    }

    [Test]
    public void OtherRpcError_StillThrows() {
        var world = new DownWorld(StatusCode.Internal);
        Assert.Throws<RpcException>(() => AcquireLock(SessionWith(world)));
    }
}

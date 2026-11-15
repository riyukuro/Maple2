using Grpc.Core;
using Maple2.Server.World.Containers;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    private static readonly AccountLocks Locks = new();

    public override Task<LockResponse> AcquireLock(LockRequest request, ServerCallContext context) {
        long token = Locks.Take(request.AccountId);
        if (token != 0) {
            return Task.FromResult(new LockResponse { Token = token });
        }
        return Task.FromResult(new LockResponse {
            Error = "Lock already held.",
        });
    }

    public override Task<LockResponse> ReleaseLock(LockRequest request, ServerCallContext context) {
        if (Locks.Release(request.AccountId, request.Token)) {
            return Task.FromResult(new LockResponse());
        }
        return Task.FromResult(new LockResponse {
            Error = "Lock not held.",
        });
    }
}

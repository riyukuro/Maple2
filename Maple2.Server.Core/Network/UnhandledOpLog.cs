using Maple2.Server.Core.Constants;

namespace Maple2.Server.Core.Network;

// A session's lines for packets no handler takes: one line per op, and at most MaxLines ops, so a client sending
// arbitrary ops writes a bounded number of lines.
public sealed class UnhandledOpLog {
    public const int MaxLines = 32;

    private readonly HashSet<RecvOp> logged = [];

    // True the first time this session sends `op` without a handler, while the session has lines left.
    public bool ShouldLog(RecvOp op) {
        lock (logged) {
            return logged.Count < MaxLines && logged.Add(op);
        }
    }
}

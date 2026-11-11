namespace Maple2.Server.Core.Network;

// Limits the "Dropped packet" log of one session: the first packets are logged one by one,
// after that one line per interval carries the count of the packets dropped since the last line.
public sealed class DropLogLimiter {
    public const int FirstLines = 5;
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly object mutex = new();
    private int logged;
    private int held;
    private DateTime lastLine;

    // True when this drop gets a log line. `count` is the number of drops the line stands for:
    // 1 for a line of its own, more for a line that also covers the drops held back before it.
    public bool ShouldLog(DateTime now, out int count) {
        lock (mutex) {
            if (logged < FirstLines) {
                logged++;
                lastLine = now;
                count = 1;
                return true;
            }

            held++;
            if (now - lastLine < Interval) {
                count = 0;
                return false;
            }

            count = held;
            held = 0;
            lastLine = now;
            return true;
        }
    }
}

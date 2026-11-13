namespace Maple2.Server.Game.Util;

// ShutdownNotice 0xAF. Only the KR client's platform hook is shown to read it (CClientPlatformNexonKorea::vfunc_26),
// so it is sent under LOCALE KR only. The client posts its countdown notices only when 30, 15 or 5 minutes are left
// (vfunc_6: 1740-1800, 840-900 and 240-300 seconds before the time).
public static class ShutdownRules {
    public static readonly int[] NoticeMinutes = [30, 15, 5];

    public static bool Sent(string locale) => locale == "KR";

    // Kind 0 (time or cancel) replaces the time the channel keeps; the other kinds leave it.
    public static long Kept(long kept, int kind, long time) => kind == 0 ? time : kept;

    // The time is sent at entry while it is ahead.
    public static bool SendAtEntry(string locale, long kept, long now) => Sent(locale) && kept > now;

    // The countdown notices the clients will still show for a shutdown in this many minutes.
    public static int[] NoticesLeft(int minutes) => NoticeMinutes.Where(notice => minutes >= notice).ToArray();
}

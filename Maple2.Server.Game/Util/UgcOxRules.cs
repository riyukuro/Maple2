using Maple2.PacketLib.Tools;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The OX quiz event of a home (UGC OX): what the client sends and the rules the server applies to it.
/// </summary>
public static class UgcOxRules {
    /// <summary>
    /// The host's settings from the create dialog (HomeAction 10, sub 0). UIQuizOXCreateDialog::OnCreate
    /// (uiquizoxcreatedialog.cpp) pops the three values of the dialog's combo boxes and sends them in this order:
    /// the entry fee (an __int64), the most entrants and the winners' share in percent (the host keeps the rest).
    /// </summary>
    public readonly record struct Settings(long Fee, int Max, int Share);

    public static Settings ReadSettings(IByteReader packet) {
        long fee = packet.ReadLong();
        int max = packet.ReadInt();
        int share = packet.ReadInt();
        return new Settings(fee, max, share);
    }
}

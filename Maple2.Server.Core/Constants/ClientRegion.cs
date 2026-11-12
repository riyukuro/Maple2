using Maple2.Model.Enum;

namespace Maple2.Server.Core.Constants;

// The region the client reports in its hello (ResponseVersion; GetRegion: KR 0, JP 1, NA 2, CN 3). The client reads
// its feature levels, item environments and texts in that region's column, so it should be the server's LOCALE.
public static class ClientRegion {
    // The region as a LOCALE name, or its number when it is none of them.
    public static string Name(short region) {
        return region is >= 0 and <= byte.MaxValue && System.Enum.IsDefined((Locale) region) ? ((Locale) region).ToString() : region.ToString();
    }

    public static bool Matches(short region, string locale) => Name(region) == locale;
}

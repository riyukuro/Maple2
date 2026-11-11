using System;

namespace Maple2.Tools;

// The client region the server and the ingested data follow: the LOCALE environment variable.
// The ingest filters feature.xml / feature_setting.xml by it, and the server's Lua formulas branch on it,
// so both read it here. Unset or empty = KR, the client's default region.
public static class Locale {
    public const string Default = "KR";
    public static readonly string[] Allowed = { "KR", "NA", "CN", "JP", "TH", "TW" };

    public static bool TryRead(out string locale, out string error) {
        string? value = Environment.GetEnvironmentVariable("LOCALE");
        locale = string.IsNullOrWhiteSpace(value) ? Default : value.Trim().ToUpperInvariant();
        if (Array.IndexOf(Allowed, locale) >= 0) {
            error = string.Empty;
            return true;
        }

        error = $"LOCALE '{value}' is not one of {string.Join(", ", Allowed)} (unset or empty = {Default})";
        return false;
    }

    public static string Read() {
        if (!TryRead(out string locale, out string error)) {
            throw new ArgumentException(error);
        }
        return locale;
    }
}

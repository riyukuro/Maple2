using System.Globalization;
using System.Text.RegularExpressions;

namespace Maple2.Model.Metadata;

// A price the player confirms in a client box comes from the client's own table/constants.xml, read with
// the client's locale. The server pack's constants.xml does not always carry the same value, so for price
// constants the client's row wins: the server must take the amount the box showed.
public static partial class ClientPriceConstants {
    [GeneratedRegex("merat|meret|meso|price|fee|cost", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PriceName();

    public readonly record struct Override(string Key, string? ServerValue, string ClientValue);

    public static bool IsPrice(string key) {
        return PriceName().IsMatch(key);
    }

    // Cooltimes the client counts down from its own table (in milliseconds: it divides by 1000 to compare with
    // the server's seconds). The server must refuse for exactly as long as the client's button is grey.
    private static readonly HashSet<string> ClientCooltimes = new(StringComparer.OrdinalIgnoreCase) {
        "HomeBankCallCooltime",
        "HomeDoctorCallCooltime",
    };

    public static bool IsClientCooltime(string key) {
        return ClientCooltimes.Contains(key);
    }

    // rows: (key, value) in file order, already filtered by locale and feature. A later row replaces an earlier one.
    public static Dictionary<string, string> Select(IEnumerable<(string Key, string Value)> rows) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in rows) {
            result[key.Trim()] = value.Trim();
        }
        return result;
    }

    // The price and cooltime constants whose client value differs from the server's (or that the server pack has no row for).
    public static List<Override> Differences(IReadOnlyDictionary<string, string> server, IReadOnlyDictionary<string, string> client) {
        var result = new List<Override>();
        foreach ((string key, string clientValue) in client.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)) {
            if (!IsPrice(key) && !IsClientCooltime(key)) {
                continue;
            }
            server.TryGetValue(key, out string? serverValue);
            if (serverValue != null && SameValue(serverValue, clientValue)) {
                continue;
            }
            result.Add(new Override(key, serverValue, clientValue));
        }
        return result;
    }

    private static bool SameValue(string a, string b) {
        if (TryNumber(a, out double x) && TryNumber(b, out double y)) {
            return x == y;
        }
        return string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
    }

    private static bool TryNumber(string text, out double value) {
        return double.TryParse(text.Trim().TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

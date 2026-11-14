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

    // Caps the client applies from its own table before it sends: a server row below the client's would drop
    // a request the client's window allows.
    private static readonly HashSet<string> ClientCaps = new(StringComparer.OrdinalIgnoreCase) {
        "ItemBoxMultiOpenMaxCount",
    };

    public static bool IsClientCap(string key) {
        return ClientCaps.Contains(key);
    }

    // The ingest option that names the Xml pack of the client that is really played (MS2_PLAYED_CLIENT_XML).
    // Not set or blank: no such pack, the data folder's Xml pack alone is "the client".
    public static string? PackOption(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // Prices the client defines in code (Client::Constants, its Define calls) and keeps when its table/constants.xml
    // has no row that passes the locale and feature filter. The server pack's row must not win there: the KR client
    // table has no MeratAirTaxiPrice row, so the world map's box asks 390 merets while the server pack's KR row says 90.
    // Only a price key the server pack sets and the client table lacks needs an entry here (KR packs: this one).
    private static readonly (string Key, string Value)[] ClientBuiltIn = [
        ("MeratAirTaxiPrice", "390"),
    ];

    // The client's price values. data: the data folder's client table rows; played: the played client's table rows
    // (MS2_PLAYED_CLIENT_XML), null when no such table was read. The built-in values belong to the client that is
    // played: they replace the data folder's rows and its own table's rows replace them. With no played table, the
    // data folder's pack is that client, so its rows replace the built-in ones. Rows in file order, filtered.
    public static Dictionary<string, string> SelectClient(IEnumerable<(string Key, string Value)> data, IEnumerable<(string Key, string Value)>? played) {
        return played == null ? Select(ClientBuiltIn.Concat(data)) : Select(data.Concat(ClientBuiltIn).Concat(played));
    }

    // rows: (key, value) in file order, already filtered by locale and feature. A later row replaces an earlier one.
    public static Dictionary<string, string> Select(IEnumerable<(string Key, string Value)> rows) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in rows) {
            result[key.Trim()] = value.Trim();
        }
        return result;
    }

    // The price, cooltime and cap constants whose client value differs from the server's (or that the server pack has no row for).
    public static List<Override> Differences(IReadOnlyDictionary<string, string> server, IReadOnlyDictionary<string, string> client) {
        var result = new List<Override>();
        foreach ((string key, string clientValue) in client.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)) {
            if (!IsPrice(key) && !IsClientCooltime(key) && !IsClientCap(key)) {
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

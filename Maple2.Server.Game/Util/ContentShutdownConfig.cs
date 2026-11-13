using System.Globalization;

namespace Maple2.Server.Game.Util;

// The contents the server turns off in the client (Gvg kind 7). The client keeps one flag per content id 1 to 116
// (CContentShutdown::Get, contentshutdown.cpp:33; End = 117) and its windows check them, for example 0x19 the guild
// PvP register (uiguilddialog.cpp:1021), 0x1a the party window (uipartydialog.cpp:896), 0x15 the cash shop
// (uicashshopdialog.cpp:367). The client has no names for the ids.
// Chosen here: all contents are on unless the operator lists ids in MS2_CONTENT_SHUTDOWN (comma separated, decimal
// or 0x hex; an id outside 1 to 116 is dropped, the client would assert on it). The list only changes the client:
// the server does not refuse a shut content's requests itself.
public static class ContentShutdownConfig {
    public const int End = 117;

    public static IReadOnlyList<short> Contents => Parse(Environment.GetEnvironmentVariable("MS2_CONTENT_SHUTDOWN"));

    public static IReadOnlyList<short> Parse(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }
        var contents = new SortedSet<short>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            bool parsed = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(part[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id)
                : int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
            if (parsed && id is > 0 and < End) {
                contents.Add((short) id);
            }
        }
        return contents.ToList();
    }
}

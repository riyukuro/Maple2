using System.Text.RegularExpressions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

/// <summary>
/// The treasure chests of a field map, as KMS2 sends them: interact objects added on the map's region spawn
/// points. A wooden chest (interact 11000004) stands on some of the points 2000-2999, a golden chest on every
/// point 3000-3999 (93000-93999 on the maps that number their points from 90000). Every five minutes the chests left are removed and a new set is added.
/// </summary>
public static partial class MapChestRules {
    public enum Kind { None, Wood, Gold }

    public const int WoodInteractId = 11000004;
    public const string Model = "MS2InteractActor";
    public const string WoodAsset = "interaction_chestA_01";
    public const string GoldAsset = "interaction_chestA_02";
    public const string NormalState = "Opened_A";
    public const string ReactableState = "Idle_A";

    // The time between two sets, as KMS2 sends them.
    public static readonly TimeSpan Cycle = TimeSpan.FromSeconds(300);

    public static Kind SpawnKind(int regionSpawnId) {
        return regionSpawnId switch {
            >= 2000 and < 3000 => Kind.Wood,
            >= 3000 and < 4000 => Kind.Gold,
            >= 93000 and < 94000 => Kind.Gold,
            _ => Kind.None,
        };
    }

    private const float BlockSize = 150f;

    // The chest's entity name: "EventCreate_" and the point's block packed in three bytes (x low). KMS2 cuts
    // each coordinate to a whole block toward zero: a point at (1230, -2220, 1345) is block (8, -14, 8).
    public static string EntityName(float x, float y, float z) {
        int block = (Block(z) << 16) | (Block(y) << 8) | Block(x);
        return $"EventCreate_{block}";
    }

    private static int Block(float coordinate) {
        return (sbyte) Math.Clamp((int) (coordinate / BlockSize), sbyte.MinValue, sbyte.MaxValue) & 0xFF;
    }

    // How many of a map's wooden chest points hold a chest in one set. KMS2 shows about a third of them;
    // a third rounded up is chosen here.
    public static int WoodCount(int points) {
        return points <= 0 ? 0 : (points + 2) / 3;
    }

    // The wooden chest points of one set: WoodCount of them, picked evenly.
    public static List<int> PickWood(IEnumerable<int> points, Random random) {
        List<int> pool = points.OrderBy(id => id).ToList();
        int count = WoodCount(pool.Count);
        var picked = new List<int>(count);
        while (picked.Count < count) {
            int index = random.Next(pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return picked;
    }

    [GeneratedRegex(@"\$map:0*(\d+)\$")]
    private static partial Regex MapToken();

    // The map a golden chest trophy belongs to: its name carries the map ("... $map:02000004$").
    public static bool TryTrophyMap(string? name, out int mapId) {
        mapId = 0;
        if (name == null) {
            return false;
        }
        Match match = MapToken().Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, out mapId);
    }

    // The golden chest interact ids a trophy counts (interact_object on a range or a list of ids), in the
    // order of the map's golden chest points. A range runs upward. A list in two runs (ids added to the map
    // later) starts with the later run, as KMS2 sends it for the one such map seen; the other two are taken
    // to do the same.
    public static int[] TrophyChestIds(ConditionMetadata condition) {
        if (condition.Type != ConditionType.interact_object || condition.Codes == null) {
            return [];
        }
        if (condition.Codes.Range is { } range) {
            if (range.Max < range.Min || range.Max - range.Min > 1000) {
                return [];
            }
            return Enumerable.Range(range.Min, range.Max - range.Min + 1).ToArray();
        }
        if (condition.Codes.Integers is not { Length: > 0 } ids) {
            return [];
        }

        var runs = new List<List<int>>();
        foreach (int id in ids.Distinct().OrderBy(id => id)) {
            if (runs.Count == 0 || runs[^1][^1] + 1 != id) {
                runs.Add([]);
            }
            runs[^1].Add(id);
        }
        runs.Reverse();
        return runs.SelectMany(run => run).ToArray();
    }

    // The interact id of each golden chest point: the n-th point (by id) holds the n-th chest id. A point
    // past the end of the ids holds none.
    public static Dictionary<int, int> GoldChests(IEnumerable<int> regionSpawnIds, IReadOnlyList<int> chestIds) {
        var result = new Dictionary<int, int>();
        int index = 0;
        foreach (int spawnId in regionSpawnIds.Where(id => SpawnKind(id) == Kind.Gold).Distinct().OrderBy(id => id)) {
            if (index >= chestIds.Count) {
                break;
            }
            result[spawnId] = chestIds[index++];
        }
        return result;
    }

    // Whether an opened object counts as an interacted object (trophies, quests). A golden chest returns with
    // every set under the same id and counts only the first time the character opens it; KMS2 keeps the opened
    // golden chest ids in the character's interacted object list.
    public static bool Counts(Kind kind, bool firstOpen) {
        return kind != Kind.Gold || firstOpen;
    }

    // The state an opened (normal) object takes when its wait is over, null to stay as it is. A chest is opened
    // once and hides whatever its resetTime; any other object resets only with a resetTime, to reactable while
    // it has reactions left.
    public static InteractState? StateAfterWait(Kind kind, int reactsLeft, int resetTime) {
        if (kind != Kind.None) {
            return InteractState.Hidden;
        }
        if (resetTime <= 0) {
            return null;
        }
        return reactsLeft > 0 ? InteractState.Reactable : InteractState.Hidden;
    }

    // The Add packet says reactable. A chest that is not (opened, in its hide time) needs its state sent after
    // the Add to a player who enters. Other added objects are sent as before.
    public static bool NeedsStateAfterAdd(Kind kind, InteractState state) {
        return kind != Kind.None && state != InteractState.Reactable;
    }

    // The exp an opened chest gives: a wooden chest its own, a golden chest more the first time this
    // character opens that chest.
    public static ExpType Exp(Kind kind, bool firstOpen) {
        return kind switch {
            Kind.Wood => ExpType.normalChest,
            Kind.Gold => firstOpen ? ExpType.rareChestFirst : ExpType.rareChest,
            _ => ExpType.none,
        };
    }
}

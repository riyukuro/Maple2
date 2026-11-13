using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The items of the survival match (item property survival="1") and the map's survival element. The client reads
// neither (ItemProperty::Load, commonfiles/itemdatastruct.cpp:1122; no client code reads the map's survival element),
// has no survival bag or tab, and the Survival packet has no item command: a picked-up item is a normal inventory
// item, and what happens to it after the match is the server's choice. Every rule here is Chosen here.
public static class SurvivalItemRules {
    // Survival items that also come from outside the match, so they stay normal items: 30001099 (an npc script
    // collects it), 30001102 (box 320000565 of interact object 32000021), 30001103 and 30001104 (event and promotion
    // boxes), 40400067 (GameEvent), 50600212 (a game shop sells it). Chosen here.
    public static readonly IReadOnlySet<int> OutsideSourceIds = new HashSet<int> {
        30001099, 30001102, 30001103, 30001104, 40400067, 50600212,
    };

    // An item that exists only for the match: it is deleted outside it and may not leave the inventory.
    public static bool IsMatchOnly(int itemId, bool survival) {
        return survival && !OutsideSourceIds.Contains(itemId);
    }

    public static bool IsMatchOnly(ItemMetadata metadata) {
        return IsMatchOnly(metadata.Id, metadata.Property.Survival);
    }

    // The match-only items among these (the inventory's tabs), in order.
    public static List<Item> MatchOnlyItems(IEnumerable<Item> items) {
        return items.Where(item => IsMatchOnly(item.Metadata)).ToList();
    }

    public const string LeftMatch = "entered a field outside the survival match";
    public const string MatchStart = "entered a survival map that starts empty (plusInven_Zero)";

    // Why the match-only items are deleted on entering a field with this survival element, or null to keep them.
    // A field without one is outside the match. A survival map with plusInven_Zero=1, read as "the match starts with
    // an empty survival inventory", drops what was left from an earlier match, but not on the re-entry of a
    // participant into the same field instance while its game there still runs. Leaving already made it fall, so it
    // comes back as a watcher; a channel change lands in another instance and clears. Chosen here.
    public static string? ClearReason(MapMetadataSurvival? survival, bool inRunningGame = false) {
        if (survival == null) {
            return LeftMatch;
        }
        return survival.PlusInvenZero == 1 && !inRunningGame ? MatchStart : null;
    }

    // The starting kit: the individual drop box a participant's items come from when the game starts
    // (plusInven=1 with plusInven_Basic, box 311000042 on the KR maps), or 0 for none. Once per participant per game.
    // Chosen here.
    public static int KitBox(MapMetadataSurvival? survival) {
        return survival is { PlusInven: 1, PlusInvenBasic: > 0 } ? survival.PlusInvenBasic : 0;
    }

    // The death drop of a participant killed while the game runs: its match-only items (userDrop=1; never the
    // OutsideSourceIds) and one roll of the userAdditionalDrop box (311000039 on the KR maps), all at the body with no
    // owner (userDropOwnership is 0 on every map). Chosen here.
    public static bool DropsOwnItems(MapMetadataSurvival? survival) {
        return survival?.UserDrop == 1;
    }

    public static int DeathBox(MapMetadataSurvival? survival) {
        return survival is { UserAdditionalDrop: > 0 } ? survival.UserAdditionalDrop : 0;
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// npcStatFactorByPlayerCount (server table): an npc spawned in a field whose dungeon room has a playerCountFactorID,
// or whose instance field has an npcStatFactorID, gets the row of that factor id for its npc class and the player
// count; each of hp / pap / map / ndd / cap becomes stat x rate + value. No row leaves the stats as they are. The
// client only reads the two ids (dungeonroom.cpp, fieldwardata.cpp), so the server alone applies them.
public static class NpcStatFactorRules {
    // Chosen here: the dungeon room's id wins over the instance field's. The room id is set per dungeon room (one map
    // can serve rooms of several difficulties), the instance field id per map. No room of the data has a field whose
    // instance field carries an id, so the order only matters for data that would add one.
    public static int FactorId(int roomFactorId, int instanceFactorId) {
        return roomFactorId != 0 ? roomFactorId : instanceFactorId;
    }

    // Chosen here: the room's player count is its size when it was made (the party's members, 1 solo) or the players
    // in the field at spawn, whichever is larger. Npcs spawn when the room is made, before anyone enters.
    public static int PlayerCount(int roomSize, int playersInField) {
        return Math.Max(1, Math.Max(roomSize, playersInField));
    }

    // The row for (factorId, npcClass, playerCount), the count capped at the largest playerCount the factor has for
    // that class. A count with no row of its own takes the nearest lower row, or the lowest row when it is below all
    // of them (factor 5001 starts at 4 players). Null: no factor id, or no row for the id and class.
    public static NpcStatFactorMetadata? Find(NpcStatFactorTable table, int factorId, int npcClass, int playerCount) {
        if (factorId == 0
            || !table.Entries.TryGetValue(factorId, out IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>? classes)
            || !classes.TryGetValue(npcClass, out IReadOnlyDictionary<int, NpcStatFactorMetadata>? counts)
            || counts.Count == 0) {
            return null;
        }

        int count = Math.Min(playerCount, counts.Keys.Max());
        if (counts.TryGetValue(count, out NpcStatFactorMetadata? row)) {
            return row;
        }
        int[] lower = counts.Keys.Where(key => key <= count).ToArray();
        return counts[lower.Length > 0 ? lower.Max() : counts.Keys.Min()];
    }

    // The npc's stats with the row applied; the other stats are copied. Results are cut to whole numbers.
    public static Dictionary<BasicAttribute, long> Apply(IReadOnlyDictionary<BasicAttribute, long> stats, NpcStatFactorMetadata row) {
        var result = new Dictionary<BasicAttribute, long>(stats);
        Scale(result, BasicAttribute.Health, row.HpRate, row.HpValue);
        Scale(result, BasicAttribute.PhysicalAtk, row.PapRate, row.PapValue);
        Scale(result, BasicAttribute.MagicalAtk, row.MapRate, row.MapValue);
        Scale(result, BasicAttribute.Defense, row.NddRate, row.NddValue);
        Scale(result, BasicAttribute.CriticalRate, row.CapRate, row.CapValue);
        return result;
    }

    private static void Scale(Dictionary<BasicAttribute, long> stats, BasicAttribute attribute, double rate, double value) {
        bool has = stats.TryGetValue(attribute, out long stat);
        if (!has && value == 0) {
            return;
        }
        stats[attribute] = (long) (stat * rate + value);
    }
}

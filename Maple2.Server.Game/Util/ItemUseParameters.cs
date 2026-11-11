namespace Maple2.Server.Game.Util;

// Parsers for item function parameters, as the client's item-use functions read them.
public static class ItemUseParameters {
    public const short QuestStartDefaultLevel = 10;

    // QuestStart: "questId[,level]"; a missing or 0 level is 10.
    public static bool TryParseQuestStart(string? parameters, out int questId, out short level) {
        questId = 0;
        level = QuestStartDefaultLevel;
        if (string.IsNullOrWhiteSpace(parameters)) {
            return false;
        }

        string[] parts = parameters.Split(',');
        if (!int.TryParse(parts[0].Trim(), out questId) || questId <= 0) {
            questId = 0;
            return false;
        }
        if (parts.Length > 1 && short.TryParse(parts[1].Trim(), out short parsed) && parsed > 0) {
            level = parsed;
        }
        return true;
    }

    // A comma separated int list; entries that are not numbers are left out.
    public static int[] ParseIntArray(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }

        var result = new List<int>();
        foreach (string part in value.Split(',')) {
            if (int.TryParse(part.Trim(), out int parsed)) {
                result.Add(parsed);
            }
        }
        return result.ToArray();
    }
}

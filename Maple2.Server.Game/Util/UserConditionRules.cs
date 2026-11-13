using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The user condition event (GameEventUserCondition) and its table (Table/userConditionEvent.xml): a row of the
// event's group is due once the character meets its condition, and then its mail is sent (with the attachments
// for every job and the character's own) and its notice shown (UserConditionEvent 1). The client only shows the
// notices; when a row is due is the server's.
// The KR conditions: level (target = the level), exp_rate (code = the level, target = the percent of that level's
// exp) and job (code = the job's code). Chosen here: a condition passed is met as well (a higher level meets an
// exp_rate row of a lower one), so that a level skipped in one step (several levels at once, a level potion) does
// not lose its row.
public static class UserConditionRules {
    public static bool Met(UserConditionEventMetadata row, int level, long expRate, int jobCode) {
        return row.Type switch {
            ConditionType.level => level >= row.Target,
            ConditionType.exp_rate => level > row.Code || (level == row.Code && expRate >= row.Target),
            ConditionType.job => jobCode == row.Code,
            _ => false,
        };
    }

    // The percent of the level's exp the character has, rounded down.
    public static long ExpRate(long exp, long nextExp) {
        return nextExp <= 0 ? 0 : Math.Max(0, exp) * 100 / nextExp;
    }

    // The attachments the character gets: those for every job (0) and those for its own (the job's code, 1 for a
    // beginner), as the notice dialog shows them.
    public static IEnumerable<UserConditionEventMetadata.Attachment> ForJob(IEnumerable<UserConditionEventMetadata.Attachment> attachments, int jobCode) {
        return attachments.Where(attachment => attachment.Job == 0 || attachment.Job == jobCode);
    }

    // The rows of the group the character meets and has not been given yet, in id order.
    public static List<UserConditionEventMetadata> Due(IEnumerable<UserConditionEventMetadata> rows, int group, ISet<int> done,
            int level, long expRate, int jobCode) {
        return rows.Where(row => row.Group == group && !done.Contains(row.Id) && Met(row, level, expRate, jobCode))
            .OrderBy(row => row.Id)
            .ToList();
    }

    public static HashSet<int> Ids(string? value) {
        var ids = new HashSet<int>();
        if (string.IsNullOrEmpty(value)) {
            return ids;
        }
        foreach (string part in value.Split(',')) {
            if (int.TryParse(part, out int id)) {
                ids.Add(id);
            }
        }
        return ids;
    }

    public static string Join(IEnumerable<int> ids) => string.Join(',', ids.OrderBy(id => id));
}

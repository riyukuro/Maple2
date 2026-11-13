using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The fame log (the fame board's history, UserEnv 12 and 13). The client lists a character's logs under the
// alliance of the log's table/famelog.xml row, with the text of the log id and the date of its time. In KMS2 a
// character holds no log id twice, no log is above the grade its alliance has at the time, and a log follows
// about one fame gain in ten.
public static class FameLogRules {
    // Chosen here: KMS2 added a log after 2 of 19 fame gains.
    public const int GAINS_PER_LOG = 10;

    // CFameTable::GetGradeByPoint (famegradedata.cpp:640): the grades in rising order, the one before the first
    // whose requirePoint is above the points, else the highest. 0 for an alliance without grades.
    public static int Grade(FameTable table, AllianceType alliance, int points) {
        int grade = 0;
        foreach (FameTable.Grade row in table.Grades.Where(row => row.Alliance == alliance).OrderBy(row => row.Level)) {
            if (points < row.RequirePoint) {
                return row.Level - 1;
            }
            grade = row.Level;
        }
        return grade;
    }

    // The grades fame_grade reports for these points: 1 to the alliance's grade (Grade). A trophy of a grade counts
    // once the alliance reaches or passes it, as the client takes a condition's fame grade from the points
    // (CQuestManager::vfunc_o10_13: GetGradeByPoint of GetFamePoints); empty for an alliance without grades.
    public static IEnumerable<int> ReachedGrades(FameTable table, AllianceType alliance, int points) {
        return Enumerable.Range(1, Grade(table, alliance, points));
    }

    // Whether this fame gain adds a log.
    public static bool Roll(Random random) {
        return random.Next(GAINS_PER_LOG) == 0;
    }

    // The log a fame gain adds: one of the alliance's logs of its grade that the character does not hold, 0 when
    // there is none.
    public static int Pick(FameTable table, AllianceType alliance, int grade, ICollection<int> held, Random random) {
        int[] ids = table.Logs
            .Where(log => log.Alliance == alliance && log.Grade == grade && !held.Contains(log.Id))
            .Select(log => log.Id)
            .Order()
            .ToArray();
        return ids.Length == 0 ? 0 : ids[random.Next(ids.Length)];
    }
}

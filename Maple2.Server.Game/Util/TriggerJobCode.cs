using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The job_code of a quest_user_detected condition: only a player of that job counts, and 0 (not given) counts every
/// player, as user_detected does. 52000188 checks the same quest and state once per job and sends each of the 11 jobs
/// to its own scene; the other placed uses (52000025 to 52000028) pair a job's own job quest with its job code.
/// </summary>
public static class TriggerJobCode {
    public static bool Matches(JobCode playerJob, int jobCode) => jobCode == 0 || (int) playerJob == jobCode;
}

using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Job 0x0025 mode 10 (skill reset): i32 rank.
// The client's reset box sends 0 for every rank, 1 for the base job's skills and 2 for the
// awakening skills (its job rank numbers), not the server's SkillRank values.
public static class JobRules {
    public static SkillRank? ResetRank(int clientRank) {
        return clientRank switch {
            0 => SkillRank.Both,
            1 => SkillRank.Basic,
            2 => SkillRank.Awakening,
            _ => null,
        };
    }

    // Job mode 2 (the job script's confirm box, sent with the npc): a job change only for a job condition row that
    // names a job to change to; the other rows (doctors, movers) have none.
    public static bool IsJobChange(JobCode changeTo) {
        return BaseJob(changeTo) != null;
    }

    public static Job? BaseJob(JobCode code) {
        return code switch {
            JobCode.Knight => Job.Knight,
            JobCode.Berserker => Job.Berserker,
            JobCode.Wizard => Job.Wizard,
            JobCode.Priest => Job.Priest,
            JobCode.Archer => Job.Archer,
            JobCode.HeavyGunner => Job.HeavyGunner,
            JobCode.Thief => Job.Thief,
            JobCode.Assassin => Job.Assassin,
            JobCode.RuneBlader => Job.RuneBlader,
            JobCode.Striker => Job.Striker,
            JobCode.SoulBinder => Job.SoulBinder,
            _ => null,
        };
    }

    // The row's job (the job the character must have now) and level; null when the change may go on.
    public static Error.JobError? AdvanceError(JobCode required, short requiredLevel, JobCode current, short level) {
        if (required != JobCode.None && current != required) {
            return Error.JobError.s_err_job_bad_job;
        }
        if (requiredLevel > 0 && level < requiredLevel) {
            return Error.JobError.s_err_job_not_enough_level;
        }
        return null;
    }

    public static SkillRank? ReadResetRank(IByteReader packet) {
        return ResetRank(packet.ReadInt());
    }
}

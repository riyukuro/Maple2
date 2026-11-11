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

    public static SkillRank? ReadResetRank(IByteReader packet) {
        return ResetRank(packet.ReadInt());
    }
}

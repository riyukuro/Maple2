using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

// A main skill of the skill tree as the server checks it: its rank, the level it starts at (1 for the job's
// base skills, else 0), its max level and the level it would have.
public readonly record struct JobSkillState(int Id, SkillRank Rank, short BaseLevel, short MaxLevel, short Level);

public enum JobSkillError {
    None,
    Points,
    MaxLevel,
    CharacterLevel,
    RequireSkill,
    RequireQuest,
}

// Job 9 (save the skill tree) and 11 (auto distribute) send the whole tree. The client sends them only after its
// own check passes (UISkillBookTreeDialog::RequestJob: SkillPointSimulator::Validate); the server repeats that
// check on the tree it would store and takes it whole or not at all.
public static class JobSkillRules {
    // JobSystem::GetLevelSkillPoint: the points a rank gets from the character level. None below level 10; the
    // base rank gets one a level from 10 up to the limit, the awakening rank one a level from 60 up to its limit.
    public static int LevelPoints(short level, SkillRank rank, int limit1, int limit2) {
        if (level < 10) {
            return 0;
        }
        return rank switch {
            SkillRank.Basic => Math.Max(0, Math.Min(level, limit1) - 9),
            SkillRank.Awakening => level < 60 ? 0 : Math.Max(0, Math.Min(level, limit2) - 59),
            _ => 0,
        };
    }

    // JobSkill::GetUsedPoint: a skill that is not learned costs nothing, a base skill its levels above 1, any
    // other learned skill its level.
    public static int UsedPoints(short level, short baseLevel) {
        return level <= 0 ? 0 : Math.Max(0, level - baseLevel);
    }

    // The tree with the sent levels: a skill that is sent takes the sent level, or its base level when sent as
    // not learned; a skill that is not sent keeps its level. A sent id that is no main skill is left out.
    public static List<JobSkillState> Apply(IEnumerable<JobSkillState> current, IEnumerable<(int Id, short Level, bool Learned)> sent) {
        var levels = new Dictionary<int, (short Level, bool Learned)>();
        foreach ((int id, short level, bool learned) in sent) {
            levels[id] = (level, learned);
        }

        var result = new List<JobSkillState>();
        foreach (JobSkillState skill in current) {
            if (!levels.TryGetValue(skill.Id, out (short Level, bool Learned) entry)) {
                result.Add(skill);
                continue;
            }
            short level = entry.Learned ? Math.Max(entry.Level, skill.BaseLevel) : skill.BaseLevel;
            result.Add(skill with { Level = level });
        }
        return result;
    }

    // SkillPointSimulator::Validate on the tree: in each rank the points spent are within the rank's points
    // (from the level, plus the extra points of that rank); each learned skill is within its max level and meets
    // what its level's upgrade element asks: the character level, other skills of the tree at a level and
    // completed quests. With limits of 0 (constants ingested before they were added) the points are not checked;
    // a level whose upgrade is null (skill metadata ingested before it was added) has no requirement checked.
    public static JobSkillError Check(IReadOnlyCollection<JobSkillState> tree, short characterLevel, int limit1, int limit2,
            IReadOnlyList<int> extraPoints, Func<int, short, SkillMetadataUpgrade?> upgrade, Func<int, bool> questCompleted) {
        if (limit1 > 0 || limit2 > 0) {
            foreach (SkillRank rank in new[] { SkillRank.Basic, SkillRank.Awakening }) {
                int extra = (int) rank < extraPoints.Count ? extraPoints[(int) rank] : 0;
                int total = LevelPoints(characterLevel, rank, limit1, limit2) + extra;
                int used = tree.Where(skill => skill.Rank == rank).Sum(skill => UsedPoints(skill.Level, skill.BaseLevel));
                if (used > total) {
                    return JobSkillError.Points;
                }
            }
        }

        var levels = tree.ToDictionary(skill => skill.Id, skill => skill.Level);
        foreach (JobSkillState skill in tree) {
            if (skill.Level <= 0) {
                continue;
            }
            if (skill.Level > skill.MaxLevel) {
                return JobSkillError.MaxLevel;
            }

            SkillMetadataUpgrade? need = upgrade(skill.Id, skill.Level);
            if (need == null) {
                continue;
            }
            if (need.Level > characterLevel) {
                return JobSkillError.CharacterLevel;
            }
            for (int i = 0; i < need.SkillIds.Length; i++) {
                int requireLevel = i < need.SkillLevels.Length ? need.SkillLevels[i] : 1;
                if (!levels.TryGetValue(need.SkillIds[i], out short level) || level <= 0 || level < requireLevel) {
                    return JobSkillError.RequireSkill;
                }
            }
            if (need.QuestIds.Any(questId => questId != 0 && !questCompleted(questId))) {
                return JobSkillError.RequireQuest;
            }
        }
        return JobSkillError.None;
    }
}

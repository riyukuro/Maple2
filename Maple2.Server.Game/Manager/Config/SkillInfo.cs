using System.Diagnostics;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager.Config;

public class SkillInfo : IByteSerializable {
    public const int SKILL_TYPES = 4;
    public const int SKILL_RANKS = 2;

    private readonly JobTable jobTable;
    private readonly SkillMetadataStorage skillMetadata;

    public Job Job { get; private set; }
    public readonly IDictionary<int, Skill>[,] Skills;
    public readonly IDictionary<int, Skill>[,] SubSkills;
    // Every skill of every job's tree, main and sub.
    private readonly HashSet<int> treeSkills;
    // The own tree's skills: a main skill to itself, a sub skill to its main skill.
    private Dictionary<int, int> mainOf = new();
    // The job table's sub skills that have no skill file, warned of once per process, not at each login: the KR job.xml
    // rows JobChange_01/02 list 10700082 under Haste (10700081), and only an additional effect has that id.
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> MissingSubSkills = new();

    public SkillInfo(JobTable jobTable, SkillMetadataStorage skillMetadata, Job job, SkillTab? skillTab) {
        this.jobTable = jobTable;
        this.skillMetadata = skillMetadata;

        Skills = new IDictionary<int, Skill>[SKILL_TYPES, SKILL_RANKS];
        SubSkills = new IDictionary<int, Skill>[SKILL_TYPES, SKILL_RANKS];
        for (int i = 0; i < SKILL_TYPES; i++) {
            for (int j = 0; j < SKILL_RANKS; j++) {
                Skills[i, j] = new Dictionary<int, Skill>();
                SubSkills[i, j] = new Dictionary<int, Skill>();
            }
        }

        treeSkills = SkillCastRules.TreeSkills(jobTable);

        SetJob(job);
        SetTab(skillTab);

        // Disable all notifications when loading.
        foreach (IDictionary<int, Skill> dict in Skills) {
            foreach (Skill skill in dict.Values) {
                skill.Notify = false;
            }
        }
    }

    public void SetJob(Job job) {
        // If job is unchanged, we do not need to update Skills.
        if (Job == job) {
            return;
        }

        if (!jobTable.Entries.TryGetValue(job.Code(), out JobTable.Entry? jobTableEntry)) {
            throw new ArgumentException($"No Table found for job: {job} => {job.Code()}");
        }

        foreach (IDictionary<int, Skill> dict in Skills) {
            dict.Clear();
        }
        foreach (IDictionary<int, Skill> dict in SubSkills) {
            dict.Clear();
        }
        mainOf = SkillCastRules.MainSkills(jobTableEntry, job.IsAwakening());

        var baseSkills = new HashSet<int>(jobTableEntry.BaseSkills);
        foreach ((SkillRank rank, JobTable.Skill[] jobSkills) in jobTableEntry.Skills) {
            Debug.Assert(rank is SkillRank.Basic or SkillRank.Awakening);
            if (rank == SkillRank.Awakening && !job.IsAwakening()) {
                continue;
            }

            foreach (JobTable.Skill skillData in jobSkills) {
                short baseLevel = (short) (baseSkills.Contains(skillData.Main) ? 1 : 0);
                var subSkills = new List<Skill>(skillData.Sub.Length);
                foreach (int subSkillId in skillData.Sub) {
                    if (!skillMetadata.TryGet(subSkillId, 1, out SkillMetadata? subMetadata)) {
                        if (MissingSubSkills.TryAdd(subSkillId, 0)) {
                            Log.Warning("Skipping invalid subSkillId:{SkillId}", subSkillId);
                        }
                        continue;
                    }

                    var subSkill = new Skill(subSkillId, baseLevel, subMetadata.Property.MaxLevel);
                    subSkills.Add(subSkill);
                    SubSkills[(int) subMetadata.Property.Type, (int) rank].Add(subSkillId, subSkill);
                }

                if (!skillMetadata.TryGet(skillData.Main, 1, out SkillMetadata? metadata)) {
                    throw new InvalidOperationException($"Nonexistent skillId:{skillData.Main}");
                }

                var skill = new Skill(skillData.Main, subSkills.ToArray(), baseLevel, metadata.Property.MaxLevel);
                Skills[(int) metadata.Property.Type, (int) rank].Add(skill.Id, skill);
                Log.Verbose("+Skill[{Type}, {Rank}] = {Id} ({Name})", metadata.Property.Type, rank, skill.Id, metadata.Name);
            }
        }

        Job = job;
    }

    public void SetTab(SkillTab? skillTab) {
        var skillMap = new Dictionary<int, (int, int)>();
        if (skillTab != null) {
            foreach ((int skillId, int points) in skillTab.Skills) {
                skillMap[skillId] = (skillId, points);
            }
        }

        foreach (IDictionary<int, Skill> dict in Skills) {
            foreach (Skill skill in dict.Values) {
                if (skillMap.TryGetValue(skill.Id, out (int SkillId, int Points) tabSkill)) {
                    skill.SetLevel((short) (skill.BaseLevel + tabSkill.Points));
                } else {
                    skill.Reset();
                }
            }
        }
    }

    /// <summary>
    /// Returns <b>Main</b> skills with specified filtering.
    /// </summary>
    /// <param name="type">Type of main skills to return.</param>
    /// <param name="rank">Rank of main skills to return.</param>
    /// <returns>Enumerable of skills that match the filter.</returns>
    public IEnumerable<Skill> GetMainSkills(SkillType type, SkillRank rank) {
        if (rank is SkillRank.Basic or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Basic].Values) {
                yield return skill;
            }
        }

        if (rank is SkillRank.Awakening or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Awakening].Values) {
                yield return skill;
            }
        }
    }

    /// <summary>
    /// Returns <b>Main</b> job-specific skills with specified filtering.
    /// </summary>
    /// <param name="type">Type of main skills to return.</param>
    /// <param name="rank">Rank of main skills to return.</param>
    /// <returns>Enumerable of skills that match the filter.</returns>
    public SortedDictionary<int, Skill> GetMainLearnedJobSkills(SkillType type, SkillRank rank) {
        return new SortedDictionary<int, Skill>(GetMainSkills(type, rank)
            .Where(skill => skill is { Level: > 0, Id: < 20000000 })
            .ToDictionary(skill => skill.Id, skill => skill));
    }

    /// <summary>
    /// Returns <b>Main</b> and <b>Sub</b> skills with specified filtering.
    /// </summary>
    /// <param name="type">Type of skills to return.</param>
    /// <param name="rank">Rank of skills to return.</param>
    /// <returns>Enumerable of skills that match the filter.</returns>
    public IEnumerable<Skill> GetSkills(SkillType type, SkillRank rank) {
        if (rank is SkillRank.Basic or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Basic].Values) {
                yield return skill;
            }
            foreach (Skill skill in SubSkills[(int) type, (int) SkillRank.Basic].Values) {
                yield return skill;
            }
        }

        if (rank is SkillRank.Awakening or SkillRank.Both) {
            foreach (Skill skill in Skills[(int) type, (int) SkillRank.Awakening].Values) {
                yield return skill;
            }
            foreach (Skill skill in SubSkills[(int) type, (int) SkillRank.Awakening].Values) {
                yield return skill;
            }
        }
    }

    public Skill? GetMainSkill(int skillId, SkillRank rank = SkillRank.Both) {
        for (int i = 0; i < SKILL_TYPES; i++) {
            if (rank is SkillRank.Basic or SkillRank.Both) {
                if (Skills[i, (int) SkillRank.Basic].TryGetValue(skillId, out Skill? skill)) {
                    return skill;
                }
            }

            if (rank is SkillRank.Awakening or SkillRank.Both) {
                if (Skills[i, (int) SkillRank.Awakening].TryGetValue(skillId, out Skill? skill)) {
                    return skill;
                }
            }
        }

        return null;
    }

    // Whether the skill is in any job's skill tree.
    public bool InAnyTree(int skillId) => treeSkills.Contains(skillId);

    // The level the own tree holds the skill at (a sub skill: its main skill's level); null when not in it.
    public short? TreeLevel(int skillId) => mainOf.TryGetValue(skillId, out int main) ? GetMainSkill(main)?.Level : null;

    public void WriteTo(IByteWriter writer) {
        // JobSystem's rank, which decides which skills the client shows as learned (rank >= the skill's
        // job level). The client computes it as JobTable::GetJobRank: the last digit of the job + 1,
        // so 1 for a first job and 2 after the awakening.
        writer.WriteInt((int) Job % 10 + 1);
        writer.WriteInt((int) Job.Code());
        int count = 0;
        for (int i = 0; i < SKILL_TYPES; i++) {
            for (int j = 0; j < SKILL_RANKS; j++) {
                count += Skills[i, j].Count + SubSkills[i, j].Count;
            }
        }
        writer.WriteByte((byte) count);
        for (int i = 0; i < SKILL_TYPES; i++) {
            for (int j = 0; j < SKILL_RANKS; j++) {
                foreach (Skill skill in Skills[i, j].Values) {
                    writer.WriteClass<Skill>(skill);
                }
                foreach (Skill subSkill in SubSkills[i, j].Values) {
                    writer.WriteClass<Skill>(subSkill);
                }
            }
        }
    }

    public class Skill : IByteSerializable {
        public readonly int Id;
        public readonly short BaseLevel;
        public readonly short MaxLevel;
        public short Level { get; private set; }
        public bool Notify;

        private readonly Skill[] subSkills;

        public Skill(int id, Skill[] subSkills, short baseLevel, short maxLevel) {
            Id = id;
            this.subSkills = subSkills;
            BaseLevel = baseLevel;
            MaxLevel = maxLevel;
            Level = baseLevel;
        }

        public Skill(int id, short baseLevel, short maxLevel) : this(id, [], baseLevel, maxLevel) { }

        public void Reset() {
            Level = BaseLevel;
            Notify = false;
        }

        public void SetLevel(short level, bool notify = true) {
            if (notify && BaseLevel == 0 && Level == 0 && level > 0) {
                Notify = notify;
            }

            Level = Math.Clamp(level, BaseLevel, MaxLevel);
            foreach (Skill subSkill in subSkills) {
                subSkill.SetLevel(level, false);
            }
        }

        // JobSkill::Read (jobsystem.cpp:129): the level, then changed, learned and extra. Changed is the skill learned
        // just now: the client's "learned skill" reward notice and the tree's newSkill mark. Extra is a skill granted
        // outside the job tree, which the skill tree leaves out (UISkillBookTreeDialog::RefreshAll); the server grants
        // none.
        public void WriteTo(IByteWriter writer) {
            writer.WriteInt(Id);
            writer.WriteInt(Math.Max((int) Level, 1));
            writer.WriteBool(Notify);
            writer.WriteBool(Level > 0);
            writer.WriteBool(false);

            Notify = false;
        }
    }
}

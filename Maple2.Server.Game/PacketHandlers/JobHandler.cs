using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class JobHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Job;

    private enum Command : byte {
        Advance = 2,
        Unknown = 7,
        Load = 8,
        Update = 9,
        Reset = 10,
        AutoDistribute = 11,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Advance:
                HandleAdvance(session, packet);
                return;
            case Command.Unknown:
                packet.ReadInt();
                packet.ReadLong();
                packet.ReadInt();
                packet.ReadShort();
                return;
            case Command.Load:
                HandleLoad(session);
                return;
            case Command.Update:
                HandleUpdate(session, packet);
                return;
            case Command.Reset:
                HandleReset(session, packet);
                return;
            case Command.AutoDistribute:
                AutoDistribute(session, packet);
                return;
        }
    }

    private void HandleAdvance(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        int npcId = packet.ReadInt();

        if (!session.Field.Npcs.TryGetValue(npcId, out FieldNpc? npc)) {
            return;
        }

        if (session.NpcScript?.Npc?.Value.Id != npc.Value.Id ||
            session.NpcScript.JobCondition == null) {
            return;
        }

        // TODO: Does awakening use this same packet? Handle if so.

        Job job = session.NpcScript.JobCondition.ChangeToJobCode switch {
            JobCode.Newbie => Job.Newbie,
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
            _ => throw new ArgumentException($"Invalid JobCode: {session.NpcScript.JobCondition.ChangeToJobCode}"),
        };

        Job currentJob = session.Player.Value.Character.Job;
        if (currentJob.Code() != job.Code()) {
            foreach (SkillTab skillTab in session.Config.Skill.SkillBook.SkillTabs) {
                skillTab.Skills.Clear();
            }
            session.ConditionUpdate(ConditionType.job_change, codeLong: (int) session.NpcScript.JobCondition.ChangeToJobCode);
        }

        session.Player.Value.Character.Job = job;
        session.Config.Skill.SkillInfo.SetJob(job);

        session.Player.Buffs.Clear();
        session.Player.Buffs.Initialize();
        session.Player.Buffs.LoadFieldBuffs();
        session.Stats.Refresh();
        session.Field.Broadcast(JobPacket.Advance(session.Player, session.Config.Skill.SkillInfo));
        session.ConditionUpdate(ConditionType.job, codeLong: (int) session.NpcScript.JobCondition.ChangeToJobCode);
        session.Player.Flag |= PlayerObjectFlag.Job;
    }

    private void HandleLoad(GameSession session) {
        session.Send(JobPacket.Load(session.Player, session.Config.Skill.SkillInfo));
    }

    private void HandleUpdate(GameSession session, IByteReader packet) {
        UpdateTree(session, packet);

        session.Send(JobPacket.Update(session.Player, session.Config.Skill.SkillInfo));
        session.Config.UpdateHotbarSkills();
        session.Config.Skill.UpdatePassiveBuffs();
    }

    private void HandleReset(GameSession session, IByteReader packet) {
        // The client sends its job rank number: 0 every rank, 1 base job, 2 awakening.
        SkillRank? rank = JobRules.ReadResetRank(packet);
        if (rank == null) {
            return;
        }
        session.Config.Skill.ResetSkills(rank.Value);

        session.Send(JobPacket.Reset(session.Player, session.Config.Skill.SkillInfo));
        session.Config.UpdateHotbarSkills();
        session.Config.Skill.UpdatePassiveBuffs();
    }

    private void AutoDistribute(GameSession session, IByteReader packet) {
        UpdateTree(session, packet);

        session.Send(JobPacket.AutoDistribute(session.Player, session.Config.Skill.SkillInfo));
        session.Config.UpdateHotbarSkills();
        session.Config.Skill.UpdatePassiveBuffs();
    }

    // Modes 9 and 11: i32 count, then per skill i32 id, i16 level, bool learned; the client sends the whole tree.
    // The tree is stored only if it passes the client's own check (JobSkillRules.Check), whole or not at all.
    // Either way the reply carries the stored tree: the client has no skill point error and re-reads it.
    private void UpdateTree(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();
        var sent = new List<(int Id, short Level, bool Learned)>(Math.Max(0, Math.Min(count, 1000)));
        for (int i = 0; i < count; i++) {
            int skillId = packet.ReadInt();
            short level = packet.ReadShort();
            bool learned = packet.ReadBool();
            sent.Add((skillId, level, learned));
        }

        SkillInfo info = session.Config.Skill.SkillInfo;
        var current = new List<JobSkillState>();
        foreach (SkillRank rank in new[] { SkillRank.Basic, SkillRank.Awakening }) {
            foreach (SkillType type in Enum.GetValues<SkillType>()) {
                foreach (SkillInfo.Skill skill in info.GetMainSkills(type, rank)) {
                    current.Add(new JobSkillState(skill.Id, rank, skill.BaseLevel, skill.MaxLevel, skill.Level));
                }
            }
        }

        List<JobSkillState> tree = JobSkillRules.Apply(current, sent);
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        JobSkillError error = JobSkillRules.Check(tree, session.Player.Value.Character.Level,
            constants.SkillPointLimitLevel1, constants.SkillPointLimitLevel2, session.Config.ExtraSkillPoints(),
            (skillId, level) => session.SkillMetadata.TryGet(skillId, level, out SkillMetadata? metadata) ? metadata.Data.Upgrade : null,
            questId => session.Quest.TryGetQuest(questId, out Quest? quest) && quest.State == QuestState.Completed);
        if (error != JobSkillError.None) {
            Logger.Information("Job skill tree refused ({Error}) for {CharacterId}", error, session.CharacterId);
            return;
        }

        foreach (JobSkillState skill in tree) {
            session.Config.Skill.UpdateSkill(skill.Id, skill.Level, skill.Level > 0);
        }
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Dungeon;

public class DungeonMission : IByteSerializable {
    public readonly DungeonMissionMetadata Metadata;
    public int Id => Metadata.Id;
    public short Score { get; private set; }
    public short Counter { get; private set; }

    /// <summary>One of the room's userMissions (scored per player); else a party mission.</summary>
    public readonly bool IsUserMission;

    public DungeonMission(DungeonMissionMetadata metadata, bool isUserMission = false) {
        Metadata = metadata;
        IsUserMission = isUserMission;
        // A penalty mission starts at its full score.
        Score = ScoreOf(metadata, 0);
    }

    /// <summary>The steps of the mission. An absent applyCount is 1 for the client (DungeonMissionData's default).</summary>
    public short ApplyCount => Math.Max((short) 1, Metadata.ApplyCount);
    /// <summary>The damage counted so far for a DamageToNpc mission.</summary>
    public long Damage { get; private set; }

    /// <summary>
    /// DungeonMissionData::GetScore: maxScore split into applyCount steps; a penalty mission loses a step's
    /// score for each count, the others gain it.
    /// </summary>
    public static short ScoreOf(DungeonMissionMetadata metadata, int count) {
        short applyCount = Math.Max((short) 1, metadata.ApplyCount);
        int unit = applyCount > 1 ? metadata.MaxScore / applyCount : metadata.MaxScore;
        int applied = Math.Clamp(count, 0, applyCount);
        int score = metadata.IsPenaltyType ? metadata.MaxScore - unit * applied : unit * applied;
        return (short) Math.Clamp(score, 0, Math.Max((short) 0, metadata.MaxScore));
    }

    /// <summary>Counts steps of the mission; false when it has all its steps.</summary>
    public bool AddCount(int count = 1) {
        if (Counter >= ApplyCount || count <= 0) {
            return false;
        }

        Counter = (short) Math.Min(ApplyCount, Counter + count);
        Score = ScoreOf(Metadata, Counter);
        return true;
    }

    /// <summary>
    /// DamageToNpc: value1 is the total damage, one step per value1 / applyCount; value2 names the npc
    /// (none: any npc, DungeonMissionDataDamageToNpc::IsTargetNpc).
    /// </summary>
    public bool AddDamage(int npcId, long amount) {
        if (Metadata.Type != DungeonMissionType.DamageToNpc || amount <= 0 || Counter >= ApplyCount) {
            return false;
        }
        if (Metadata.Value2 != 0 && Metadata.Value2 != npcId) {
            return false;
        }

        Damage += amount;
        long total = Metadata.Value1.Length > 0 ? Metadata.Value1[0] : 0;
        long perApply = total / ApplyCount;
        int count = perApply <= 0 ? ApplyCount : (int) Math.Min(ApplyCount, Damage / perApply);
        return AddCount(count - Counter);
    }

    /// <summary>LastHitNpc: a step when the player's hit kills one of the npcs in value1.</summary>
    public bool AddLastHit(int npcId) {
        if (Metadata.Type != DungeonMissionType.LastHitNpc || !Metadata.Value1.Contains(npcId)) {
            return false;
        }
        return AddCount();
    }

    /// <summary>DeathCount: a step for each death (the client shows the deaths left of applyCount).</summary>
    public bool AddDeath() {
        if (Metadata.Type != DungeonMissionType.DeathCount) {
            return false;
        }
        return AddCount();
    }

    /// <summary>
    /// DamageBySkill: a step for each hit by one of the skills in value1. value2 is the skill levels the client
    /// reads; with one given, only a hit at that level counts.
    /// </summary>
    public bool AddSkillHit(int skillId, short level) {
        if (Metadata.Type != DungeonMissionType.DamageBySkill || !Metadata.Value1.Contains(skillId)) {
            return false;
        }
        if (Metadata.Value2 != 0 && Metadata.Value2 != level) {
            return false;
        }
        return AddCount();
    }

    /// <summary>GainBuff: a step for each gain of one of the buffs in value1.</summary>
    public bool AddBuff(int buffId) {
        if (Metadata.Type != DungeonMissionType.GainBuff || !Metadata.Value1.Contains(buffId)) {
            return false;
        }
        return AddCount();
    }

    /// <summary>
    /// PlayTime: the full score when the room is cleared within value2 seconds (the "within N min" of every
    /// mission name). value1 is not needed for it.
    /// </summary>
    public bool ScorePlayTime(long seconds) {
        if (Metadata.Type != DungeonMissionType.PlayTime || seconds < 0 || seconds > Metadata.Value2 || Counter >= ApplyCount) {
            return false;
        }
        Complete();
        return true;
    }

    public void Complete() {
        Counter = ApplyCount;
        Score = Metadata.MaxScore;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteShort(Score);
        // The client picks the reader by the mission's type: every type reads the score, the play time
        // type then reads two longs, the user count type nothing more, all others the counter.
        switch (Metadata.Type) {
            case DungeonMissionType.PlayTime:
                writer.WriteLong();
                writer.WriteLong();
                break;
            case DungeonMissionType.LimitUserCount:
                break;
            default:
                writer.WriteShort(Counter);
                break;
        }
    }
}

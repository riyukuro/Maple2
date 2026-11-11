using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The rules of an AI summon node (&lt;node name="summon" .../&gt;), read from the 5072 summon nodes in 255 server AI
/// files and the 531 npc rows whose aiInfo has summonNpcIDs.
/// </summary>
public static class AiSummon {
    // npcId is a 1-based index into the summoner's summonNpcIDs: the nodes use 1 to 48 and never 0, and the
    // longest summonNpcIDs list has 48 ids. AI_Default_Summon.xml (one template for many rows) takes npcId="1".
    public static int? NpcId(int[]? summonNpcIds, int index) {
        if (summonNpcIds == null || index < 1 || index > summonNpcIds.Length) {
            return null;
        }
        return summonNpcIds[index - 1];
    }

    // npcCount summons per run of the node, while at most npcCountMax of the summoner's summons of the node's group
    // are alive (Slave nodes with npcCount 3 and npcCountMax 30 add 3 a run up to 30).
    public static int Count(int npcCount, int npcCountMax, int aliveInGroup) {
        int count = Math.Max(npcCount, 1);
        int max = npcCountMax > 0 ? npcCountMax : count;
        return Math.Clamp(max - aliveInGroup, 0, count);
    }

    // summonPos is a map position (no node gives both it and summonPosOffset; its Z values are the floor heights of
    // the boss rooms, 1500 to 5410); otherwise the summon stands at the summoner plus summonPosOffset. summonRadius
    // scatters it on the ground (X and Y); Z is not used, so no summon is put in the air.
    public static Vector3 Position(Vector3 summoner, Vector3 summonPos, Vector3 summonPosOffset, Vector3 summonRadius, Random random) {
        Vector3 position = (summonPos != Vector3.Zero ? summonPos : summoner) + summonPosOffset;
        if (summonRadius.X > 0 || summonRadius.Y > 0) {
            float angle = random.NextSingle() * MathF.PI * 2;
            float distance = MathF.Sqrt(random.NextSingle());
            position.X += MathF.Cos(angle) * distance * MathF.Max(summonRadius.X, 0);
            position.Y += MathF.Sin(angle) * distance * MathF.Max(summonRadius.Y, 0);
        }
        return position;
    }

    // summonRot for a summon at a map position or a node that gives one; a summon next to the summoner without one
    // faces the way the summoner does.
    public static Vector3 Rotation(Vector3 summoner, Vector3 summonPos, Vector3 summonRot) {
        return summonRot != Vector3.Zero || summonPos != Vector3.Zero ? summonRot : summoner;
    }

    // lifeTime has two units in the AI files: a value under 1000 is seconds, one of 1000 or more milliseconds (a
    // server choice). The values fall in two groups, 14..80 (the 30 and 60 of the Blackbean phases would end the
    // summon in the tick it appears as milliseconds) and 3000..180000 (as seconds 3000 s and more, longer than any
    // fight), so both read as 3 s to 180 s. AI_DevilHugeRed's lifeTime="15.6" is read by the file parser as 15600,
    // 15.6 s, the literal read as seconds; its 14 other summons give 6000, 6 s. 0 is no limit.
    public const int LifeTimeMillisecondsFrom = 1000;

    public static TimeSpan LifeTime(int lifeTime) => lifeTime switch {
        <= 0 => TimeSpan.Zero,
        < LifeTimeMillisecondsFrom => TimeSpan.FromSeconds(lifeTime),
        _ => TimeSpan.FromMilliseconds(lifeTime),
    };

    // delayTick is milliseconds, like every other tick in the AI files (cooltime 10000, sayNode durationTick).
    public static TimeSpan Delay(int delayTick) => delayTick > 0 ? TimeSpan.FromMilliseconds(delayTick) : TimeSpan.Zero;

    // master="None" (288 nodes) is a summon with no master: it stays when the summoner goes. Every other node
    // (no master attribute, or master="Slave") leaves with its summoner.
    public static bool LeavesWithSummoner(NodeSummonMaster master) => master != NodeSummonMaster.None;

    // A slave is a summon that leaves with its summoner. The SlaveCount conditions and the RemoveSlaves node read the
    // slaves only; a master="None" summon is no one's slave. The AI files' comments on RemoveSlaves say it removes
    // every monster this npc summoned as its slaves, even in the middle of an action.
    // useSummonGroup="1" counts the slaves of summonGroup only (AI_Primadonna: group 201, npcCountMax 2).
    public static bool IsCountedSlave(int summonGroup, bool leavesWithSummoner, int? group) =>
        leavesWithSummoner && (group == null || summonGroup == group);

    // SlaveCount count="N" passes when exactly N slaves are alive. The Yeti bosses test 0, 1, 2, 3, 4 in that order in
    // one conditions node (first passing wins), so ">= N" would always take 0; AI_Primadonna tests 2 then 1 with
    // npcCountMax 2, so "<= N" would never reach its count="1" (berserk) branch. Only "== N" fits both.
    public static bool SlaveCountPasses(int count, int slaves) => slaves == count;

    // slaveCount="N" slaveCountOp="Op" compares the slaves alive with N (Seolnunyi: slaveCount="0"
    // slaveCountOp="Greater" keeps battling while any slave lives).
    public static bool SlaveCountOpPasses(AiConditionOp op, int value, int slaves) => op switch {
        AiConditionOp.Equal => slaves == value,
        AiConditionOp.Greater => slaves > value,
        AiConditionOp.Less => slaves < value,
        AiConditionOp.GreaterEqual => slaves >= value,
        AiConditionOp.LessEqual => slaves <= value,
        _ => false,
    };
}

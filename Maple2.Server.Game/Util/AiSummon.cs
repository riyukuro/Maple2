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

    // lifeTime is in seconds: the values run from 14 (and 15.6) to 180000. In milliseconds the 30 and 60 of the
    // Blackbean phases would remove the summon in the tick it appears; the large values (99900, 180000) last
    // longer than any fight. 0 is no limit.
    public static TimeSpan LifeTime(int lifeTime) => lifeTime > 0 ? TimeSpan.FromSeconds(lifeTime) : TimeSpan.Zero;

    // delayTick is milliseconds, like every other tick in the AI files (cooltime 10000, sayNode durationTick).
    public static TimeSpan Delay(int delayTick) => delayTick > 0 ? TimeSpan.FromMilliseconds(delayTick) : TimeSpan.Zero;

    // master="None" (288 nodes) is a summon with no master: it stays when the summoner goes. Every other node
    // (no master attribute, or master="Slave") leaves with its summoner.
    public static bool LeavesWithSummoner(NodeSummonMaster master) => master != NodeSummonMaster.None;
}

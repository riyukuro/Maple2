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
    // option="masterHP" (62 summon nodes in npc AI files: the Gigantika, Kandura, Madria and Balrog phase bodies,
    // each summoning its next body before RemoveMe): the summon takes over its summoner's health as it is (KR
    // AI_TurkaHoodForce_Phase01.xml comments masterHP as inheriting the summoner's HP value unchanged), within its own
    // full health and at least 1. Null without the option: the summon keeps its own full health.
    public static long? InheritedHealth(NodeSummonOption[] option, long summonerHealth, long summonTotal) {
        if (Array.IndexOf(option, NodeSummonOption.MasterHp) < 0) {
            return null;
        }
        return Math.Clamp(summonerHealth, Math.Min(1, summonTotal), summonTotal);
    }

    // option="linkHP" (8 summon nodes in npc AI files, all with masterHP: the TurkaHoodForce phases and the quest
    // copy): the summon and its summoner share one health. KR AI_TurkaHoodForce_Phase01.xml comments linkHP as health
    // shared with the summoner; Phase02 summons its four copies early into a hidden corner of the map so the shared
    // health holds for their whole life. Each member's change since the last share (a hit, a dot, a heal) adds to the
    // shared health, which stays within 0 and the summoner's full health (owner total); at 0 every member dies.
    // floor: the health damage cannot take the link under, the highest of its members' own (1 for a no_die member,
    // an AI MinimumHp floor), as NpcTargetRules.FloorChange holds one npc: each hit is floored per member, but two
    // hits on two members in one share add up, and every KR linkHP npc (23000120-23000135) is no_die, whose phases
    // step on hpLess 1. Health already at or under the floor takes no damage.
    public static bool LinksHealth(NodeSummonOption[] option) => Array.IndexOf(option, NodeSummonOption.LinkHp) >= 0;

    public static long SharedHealth(long shared, long ownerTotal, IEnumerable<(long Current, long Seen)> members, long floor = 0) {
        long change = members.Sum(member => member.Current - member.Seen);
        if (floor > 0 && change < 0) {
            change = Math.Max(change, Math.Min(0, floor - shared));
        }
        return Math.Clamp(shared + change, 0, Math.Max(ownerTotal, 0));
    }

    // A linked member's own floor for SharedHealth.
    public static long MemberFloor(bool noDie, long minimumHealth) => Math.Max(noDie ? 1 : 0, minimumHealth);

    public static bool LeavesWithSummoner(NodeSummonMaster master) => master != NodeSummonMaster.None;

    // A slave is a summon that leaves with its summoner. The SlaveCount conditions and the RemoveSlaves node read the
    // slaves only; a master="None" summon is no one's slave. The AI files' comments on RemoveSlaves say it removes
    // every monster this npc summoned as its slaves, even in the middle of an action.
    // useSummonGroup="1" counts the slaves of summonGroup only (AI_Primadonna: group 201, npcCountMax 2).
    public static bool IsCountedSlave(int summonGroup, bool leavesWithSummoner, int? group) =>
        leavesWithSummoner && (group == null || summonGroup == group);

    // SetSlaveValue and SetMasterValue (setValue's convention): isModify adds the value to a key the npc already has;
    // otherwise, and for a key it lacks, the value is set.
    public static int ModifiedValue(bool hasOld, int oldValue, int value, bool isModify) =>
        isModify && hasOld ? oldValue + value : value;

    // SetSlaveValue sets the key on every live slave; isRandom (5 nodes, AI_BarkhantBlue and BarkhantBlue_Quest,
    // whose comment says to tell one of its two clones at random) on one slave picked at random.
    public static IReadOnlyList<T> ValueTargets<T>(IReadOnlyList<T> slaves, bool isRandom, Random random) {
        if (!isRandom || slaves.Count <= 1) {
            return slaves;
        }
        return [slaves[random.Next(slaves.Count)]];
    }

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

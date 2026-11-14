namespace Maple2.Model.Metadata;

// table/Server/{locale}/WorldChampionBase.xml: <WorldChampion seasonID fieldID spawnPointID activeBegin activeEnd
// rewardDamage systemMailID><Step StepID npcID rewardID/></WorldChampion>, the world raid boss (Mushking) of a
// season. One boss shares its health over every channel; a step's npc that loses its health makes the next step's
// npc appear. rewardID is a reward content row: the client shows it as the reward of the steps cleared up to that
// step (s_world_champion_reward_title, uiworldchampionrewarddialog.cpp:315-345). Begin and End are local times.
public record WorldChampionTable(IReadOnlyDictionary<int, WorldChampionTable.Season> Entries) : ServerTable {
    public record Season(
        int Id,
        int MapId,
        int SpawnPointId,
        DateTime Begin,
        DateTime End,
        long RewardDamage,
        int SystemMailId,
        IReadOnlyList<Step> Steps);

    public record Step(int Id, int NpcId, int RewardId);
}

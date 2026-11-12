namespace Maple2.Model.Validators;

// Mission points: the completeReward missionPoint of a quest, which the client lists in the daily mission dialog as
// the mission's point reward (CQuestMission complete reward, s_daily_mission_reward_mission_point).
public static class MissionPointRules {
    // The points a completed quest adds to the mission_point rows. The rows (70900001 to 70900003 for 100, 200 and
    // 400 points, 70900011 to 70900013 for 300, 600 and 900) have no code and no target, so every completed quest
    // counts with the points of its reward, and a quest without points counts nothing.
    public static int PointsCounted(int rewardMissionPoint) => rewardMissionPoint > 0 ? rewardMissionPoint : 0;
}

namespace Maple2.Model.Validators;

// useropen_minigame_clear: the row reads "complete any mini-game and claim your reward" (70200061) and lists the
// three mini-game fields a player can open (81000001, 81000002, 81000003) as its codes. Their triggers end a game
// with MiniGameGiveReward of the content type below for the players still in the winner box.
public static class MiniGameClearRules {
    public const string UserOpenReward = "UserOpenMiniGameExtraReward";

    // A reward given by a mini-game trigger counts as a cleared user-opened mini-game when it is that content's.
    public static bool CountsAsUserOpenClear(string contentType) => contentType == UserOpenReward;

    // The games of those three fields (gameName of their StartMiniGame and EndMiniGameRound actions).
    public static bool IsUserOpenGame(string gameName) =>
        gameName is "UserMassive_Crazyrunner" or "UserMassive_Springbeach" or "UserMassive_Escape";

    // The extra reward (reward content 1008) is paid at most userMassiveExtraRewardMax times a day; the triggers'
    // own comment says "5 extra bonuses a day" and the constant is 5.
    public static bool CanPayExtraReward(int paidToday, int maxPerDay) => paidToday < maxPerDay;

    // PlayerHost modes 4 and 5 (CSceneStateGame::FUN_14173b5a0) read two uints a and b, drop the notice if a > b
    // and format s_msg_item_open_massive_extra_reward_start/_clear with {0} = a and {1} = b - a. Every locale's
    // text names {0} the daily count and {1} the ones left (KR "a day {0} times, {1} left", en "({1}/{0} per day)"),
    // so a is the daily maximum and b the maximum plus the rewards left.
    public static (int A, int B) NoticeArgs(int paidToday, int maxPerDay) {
        int left = Math.Max(0, maxPerDay - paidToday);
        return (maxPerDay, maxPerDay + left);
    }
}

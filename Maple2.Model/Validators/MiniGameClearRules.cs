namespace Maple2.Model.Validators;

// useropen_minigame_clear: the row reads "complete any mini-game and claim your reward" (70200061) and lists the
// three mini-game fields a player can open (81000001, 81000002, 81000003) as its codes. Their triggers end a game
// with MiniGameGiveReward of the content type below for the players still in the winner box.
public static class MiniGameClearRules {
    public const string UserOpenReward = "UserOpenMiniGameExtraReward";

    // A reward given by a mini-game trigger counts as a cleared user-opened mini-game when it is that content's.
    public static bool CountsAsUserOpenClear(string contentType) => contentType == UserOpenReward;
}

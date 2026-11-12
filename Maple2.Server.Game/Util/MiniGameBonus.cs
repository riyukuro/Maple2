using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Util;

// The mini game bonus reward (MiniGameBonusReward game events): while such an event names a mini game, each winner
// of the game, a player in the winner box who still holds the game's record at end_mini_game (the ones the end
// reports minigame_clear for), gets the event's items. The names are the game_name of the scripts' end_mini_game.
// Chosen here: the client has no class for the event and the result window only lists items, so the rule is the
// server's. Each running event that names the game gives its items once per end, to the bag or by mail, and they
// are listed in the result window with the game's own rewards. A player whose record mini_game_give_reward or
// end_mini_game_round already ended (the losers, a game's second end) gets nothing.
public static class MiniGameBonus {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(MiniGameBonus));

    // The running events that name the game, lowest id first.
    public static IEnumerable<GameEvent> For(IEnumerable<GameEvent> events, string gameName) {
        if (string.IsNullOrEmpty(gameName)) {
            return [];
        }
        return events
            .Where(gameEvent => gameEvent.Metadata.Data is MiniGameBonusReward bonus
                && bonus.GameNames.Contains(gameName, StringComparer.OrdinalIgnoreCase))
            .OrderBy(gameEvent => gameEvent.Id);
    }

    public static void Give(GameSession session, MiniGameUserRecord record, string gameName) {
        foreach (GameEvent gameEvent in For(session.FindEvent(GameEventType.MiniGameBonusReward), gameName)) {
            if (gameEvent.Metadata.Data is not MiniGameBonusReward bonus) {
                continue;
            }
            foreach (RewardItem rewardItem in bonus.Items) {
                Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
                if (item == null) {
                    Logger.Error("Mini game bonus event {EventId}: item {ItemId} could not be made", gameEvent.Id, rewardItem.ItemId);
                    continue;
                }
                if (session.Item.AddOrMail(item, true)) {
                    record.RewardItems.Add(rewardItem);
                } else {
                    Logger.Error("Mini game bonus event {EventId}: item {ItemId} went to neither bag nor mail", gameEvent.Id, rewardItem.ItemId);
                }
            }
        }
    }
}

using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Manager.Field;

// The Spring Farm arcade game of a field (SpringFarmGame): the score dialog and the life bar follow the game, and
// its end gives each player of the game (in the field at start_game and still in it) the reward row of the rounds
// cleared and the result.
public partial class FieldManager {
    public void SpringFarmScoreChanged() {
        if (SpringFarm is { } game) {
            Broadcast(ArcadePacket.Score(game.Score));
        }
    }

    // An interact object was used: its score, when the game gives it one (once per object for one that keeps its
    // state).
    public void SpringFarmInteracted(int interactId, string entityId = "", bool keepsState = false) {
        if (SpringFarm?.Interacted(interactId, entityId, keepsState) == true) {
            SpringFarmScoreChanged();
        }
    }

    // A player died in the game: one life is lost, and the last one ends the game.
    public void SpringFarmDied() {
        if (SpringFarm is not { } game || !game.Died()) {
            return;
        }
        Broadcast(ArcadePacket.Life(game.Lives, game.LifeCount));
        if (game.Lives == 0) {
            EndSpringFarm();
        }
    }

    // The reward of the rounds cleared (table/Server/arcadeReward.xml): the arcade exp curve at the player's level
    // times the row's rate, and the row's meso. Then the game's score is kept as the player's best of the arcade
    // season when it is higher (ArcadeBest.Replaces), and the result shows the best before the game.
    public void EndSpringFarm() {
        if (SpringFarm is not { } game || !game.End()) {
            return;
        }
        SpringFarmGame.Reward reward = SpringFarmGame.RewardOf(ServerTableMetadata.ArcadeRewardTable, game.RoundsCleared);
        DateTime nowTime = DateTime.Now;
        int seasonId = ArcadeBest.SeasonAt(TableMetadata.SeasonDataTable.Arcade, nowTime);
        long now = nowTime.ToEpochSeconds();
        foreach (FieldPlayer player in Players.Values) {
            if (!game.Plays(player.Value.Character.Id)) {
                continue;
            }
            long exp = reward.ExpRate > 0 ? player.Session.Exp.AddExp(ExpType.arcade, reward.ExpRate) : 0;
            if (reward.Meso > 0) {
                player.Session.Currency.Meso += reward.Meso;
            }
            ArcadeBest? best;
            // Without an arcade season there is no best to keep (the lobby lists none).
            using (GameStorage.Request db = GameStorage.Context()) {
                best = seasonId == 0 ? null : db.GetArcadeBest(player.Value.Character.Id, SpringFarmGame.ArcadeId, seasonId);
                if (seasonId != 0 && ArcadeBest.Replaces(best, game.Score)) {
                    db.SaveArcadeScore(player.Value.Character.Id, SpringFarmGame.ArcadeId, seasonId, game.Score, now);
                }
            }
            player.Session.Send(ArcadePacket.Result(game.Score, best?.Score ?? 0, (int) Math.Min(exp, int.MaxValue), reward.Meso, 0, []));
        }
    }
}

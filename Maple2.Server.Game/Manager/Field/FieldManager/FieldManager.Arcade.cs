using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Manager.Field;

// The arcade games of a field (SpringFarmGame, BoomBoomOceanGame, ThreeTwoOneGame): the score dialog and the life
// bar follow the game, and its end gives each player of the game (in the field at start_game and still in it) the
// reward row of the rounds cleared and the result, then closes the life bar (HideAndSeek 3).
public partial class FieldManager {
    // The portal the Boom Boom Ocean and Three Two One scripts move the players to at the end: it stands in the box
    // their fail script watches, which then takes the players out of the field.
    public const int ArcadeFailPortal = 44;

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

    public void EndSpringFarm() {
        if (SpringFarm is { } game) {
            EndArcade(game);
        }
    }

    // Boom Boom Ocean: a hit by a skill or a buff the game scores, on a game player. A negative score also takes a
    // life; when the hit also kills (killed), the death takes no second one.
    public void BoomBoomOceanScored(FieldPlayer player, int id, bool killed = false) {
        if (BoomBoomOcean is not { } game || !game.Plays(player.Value.Character.Id) || !game.Scored(id, out bool lostLife)) {
            return;
        }
        Broadcast(ArcadePacket.Score(game.Score));
        if (lostLife) {
            if (killed) {
                game.KillingHitTookLife(player.Value.Character.Id);
            }
            ArcadeLifeLost(game);
        }
    }

    public void BoomBoomOceanDied(FieldPlayer player) {
        if (BoomBoomOcean is { } game && game.Plays(player.Value.Character.Id) && game.Died(player.Value.Character.Id)) {
            ArcadeLifeLost(game);
        }
    }

    // The script's end_game (after the last round, or a player in the fail box). A round still open pays no time
    // score (BoomBoomOceanGame).
    public void EndBoomBoomOcean() {
        if (BoomBoomOcean is { Ended: false } game) {
            EndArcade(game);
        }
    }

    // Three Two One: the players' picks are decided at result_round; result_round2 gives the score of a won round
    // or takes a life.
    public void ThreeTwoOnePicked(FieldPlayer player, int direction) {
        ThreeTwoOne?.Pick(player.Value.Character.Id, direction);
    }

    public bool ThreeTwoOneResult(int direction) {
        return ThreeTwoOne?.ResultRound(direction, Players.Values.Select(player => player.Value.Character.Id)) == true;
    }

    public void ThreeTwoOneResult2() {
        if (ThreeTwoOne is not { } game) {
            return;
        }
        int scoreBefore = game.Score;
        bool lostLife = game.ResultRound2();
        if (game.Score != scoreBefore) {
            Broadcast(ArcadePacket.Score(game.Score));
        }
        Broadcast(ArcadePacket.Streak(game.Streak));
        if (lostLife) {
            ArcadeLifeLost(game);
        }
    }

    public void EndThreeTwoOne() {
        if (ThreeTwoOne is { } game) {
            EndArcade(game);
        }
    }

    // A life lost in a Boom Boom Ocean or Three Two One game: the last one ends the game, and the players go to the
    // portal of the fail box, as the scripts' own end does.
    private void ArcadeLifeLost(ArcadeGame game) {
        Broadcast(ArcadePacket.Life(game.Lives, game.LifeCount));
        if (game.Lives > 0) {
            return;
        }
        EndArcade(game);
        if (!TryGetPortal(ArcadeFailPortal, out FieldPortal? portal)) {
            return;
        }
        foreach (FieldPlayer player in Players.Values) {
            if (game.Plays(player.Value.Character.Id)) {
                player.MoveToPortal(portal);
            }
        }
    }

    // A game player who leaves before the end gets no Result (mode 2, which closes the score dialog): the score dialog
    // closes with mode 13 (CloseDialog 0x4ee4, scenestategame.cpp:8289) and the life bar with HideAndSeek 3, as at
    // the end.
    public void ArcadeLeave(FieldPlayer player) {
        foreach (ArcadeGame? game in new ArcadeGame?[] { SpringFarm, BoomBoomOcean, ThreeTwoOne }) {
            if (game?.OpenFor(player.Value.Character.Id) != true) {
                continue;
            }
            player.Session.Send(ArcadePacket.CloseScore());
            player.Session.Send(HideAndSeekPacket.End(player.ObjectId));
            return;
        }
    }

    // The reward of the rounds cleared (table/Server/arcadeReward.xml): the arcade exp curve at the player's level
    // times the row's rate, and the row's meso. Then the game's score is kept as the player's best of the arcade
    // season when it is higher (ArcadeBest.Replaces), and the result shows the best before the game.
    // The life bar closes with HideAndSeek 3 for the player's own object, which also shows the hud again.
    private void EndArcade(ArcadeGame game) {
        if (!game.End()) {
            return;
        }
        ArcadeGame.Reward reward = game.RewardOf(ServerTableMetadata.ArcadeRewardTable);
        DateTime nowTime = DateTime.Now;
        int seasonId = ArcadeBest.SeasonAt(TableMetadata.SeasonDataTable.Arcade, nowTime);
        long now = nowTime.ToEpochSeconds();
        foreach (FieldPlayer player in Players.Values) {
            if (!game.Plays(player.Value.Character.Id)) {
                continue;
            }
            long exp = reward.ExpRate > 0 ? player.Session.Exp.AddExp(ExpType.arcade, reward.ExpRate) : 0;
            if (reward.Meso > 0) {
                player.Session.Currency.ChangeMeso(reward.Meso);
            }
            ArcadeBest? best;
            // Without an arcade season there is no best to keep (the lobby lists none).
            using (GameStorage.Request db = GameStorage.Context()) {
                best = seasonId == 0 ? null : db.GetArcadeBest(player.Value.Character.Id, game.Id, seasonId);
                if (seasonId != 0 && ArcadeBest.Replaces(best, game.Score)) {
                    db.SaveArcadeScore(player.Value.Character.Id, game.Id, seasonId, game.Score, now);
                }
            }
            player.Session.Send(ArcadePacket.Result(game.Score, best?.Score ?? 0, (int) Math.Min(exp, int.MaxValue), reward.Meso, 0, GiveEventRewards(player, game.Id, game.RoundsCleared)));
            player.Session.Send(HideAndSeekPacket.End(player.ObjectId));
        }
    }

    // An event arcade's reward (ArcadeEvent): the reward rows of the event that opens the arcade, while the player
    // is under its count for the day and the game cleared a round, each to the bag or by mail. The items given are listed in the result dialog.
    private List<RewardItem> GiveEventRewards(FieldPlayer player, int arcadeId, int roundsCleared) {
        var given = new List<RewardItem>();
        if (!TableMetadata.ArcadeGameTable.Entries.TryGetValue(arcadeId, out ArcadeGameTable.Entry? entry) || !entry.IsEventArcade
            || ArcadeEvent.Open(player.Session.FindEvent(GameEventType.ArcadeOpen), arcadeId) is not { } gameEvent
            || gameEvent.Metadata.Data is not ArcadeOpen arcadeOpen) {
            return given;
        }
        RewardItem[] due = ArcadeEvent.RewardsDue(arcadeOpen, ArcadeEvent.RewardCount(player.Session, gameEvent), roundsCleared);
        if (due.Length == 0) {
            return given;
        }
        if (!ArcadeEvent.AddReward(player.Session, gameEvent)) {
            logger.Error("Arcade event {EventId}: the count of {CharacterId} was not stored", gameEvent.Id, player.Value.Character.Id);
        }
        foreach (RewardItem rewardItem in due) {
            Item? item = ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                logger.Error("Arcade event {EventId}: item {ItemId} could not be made", gameEvent.Id, rewardItem.ItemId);
                continue;
            }
            if (player.Session.Item.AddOrMail(item, true)) {
                given.Add(rewardItem);
            } else {
                logger.Error("Arcade event {EventId}: item {ItemId} went to neither bag nor mail", gameEvent.Id, rewardItem.ItemId);
            }
        }
        return given;
    }
}

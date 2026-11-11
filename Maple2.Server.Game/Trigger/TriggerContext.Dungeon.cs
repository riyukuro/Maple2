using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void DungeonClear(string uiType) {
        DebugLog("[DungeonClear] uiType:{UiType}", uiType);
        if (Field is not DungeonFieldManager dungeonField) {
            return;
        }

        // uiType None: no clear banner, but the room is cleared (the Colosseum ends a run this way when a round
        // after the first is lost, and the rounds cleared so far count).
        dungeonField.ChangeState(Maple2.Model.Enum.DungeonState.Clear, showResult: ClearShowsResult(uiType));
    }

    public static bool ClearShowsResult(string uiType) => uiType != "None";

    public void DungeonClearRound(int round) {
        DebugLog("[DungeonClearRound] round:{Round}", round);

        foreach (FieldPlayer player in Field.Players.Values) {
            if (player.Session.Dungeon.UserRecord is null) {
                continue;
            }

            player.Session.Dungeon.UserRecord.Round = round;
        }
    }

    // The room timer actions follow the shipped scripts' own comments on them: set_end_time ends the
    // time function, close_timer closes the timer window, stop_timer stops the time (for the mission
    // rank time), reset_time runs the timer again for the given seconds, set_lap_time places a mark on
    // the gauge timer (milliseconds from the start), move_lap_time_to_now moves a mark to the present.
    // Without a room timer they do nothing.
    public void DungeonCloseTimer() {
        DebugLog("[DungeonCloseTimer]");
        Field.RoomTimer?.Close();
    }

    // The room's run is left off the Colosseum board.
    public void DungeonDisableRanking() {
        DebugLog("[DungeonDisableRanking]");
        if (Field is DungeonFieldManager dungeonField) {
            dungeonField.DungeonRoomRecord.DisableRanking();
        }
    }

    public void DungeonEnableGiveUp(bool enabled) {
        DebugLog("[DungeonEnableGiveUp] enabled:{Enabled}", enabled);
        Field.Broadcast(DungeonMissionPacket.SetAbandon(enabled));
    }

    // The room's result is Fail: every player gets the result packet, which the client shows as the
    // s_room_dungeon_fail banner (result 2). A room that already has a result keeps it.
    public void DungeonFail() {
        DebugLog("[DungeonFail]");
        if (Field is not DungeonFieldManager dungeonField || !CanFailDungeon(dungeonField.DungeonRoomRecord.State)) {
            return;
        }

        dungeonField.ChangeState(Maple2.Model.Enum.DungeonState.Fail);
    }

    public static bool CanFailDungeon(Maple2.Model.Enum.DungeonState state) => state == Maple2.Model.Enum.DungeonState.None;

    public void DungeonMissionComplete(string feature, int missionId) {
        DebugLog("[DungeonMissionComplete] missionId:{MissionId}, feature:{Feature}", missionId, feature);

        foreach (FieldPlayer player in Field.Players.Values) {
            if (player.Session.Dungeon.UserRecord is null) {
                continue;
            }
            if (!player.Session.Dungeon.UserRecord.Missions.TryGetValue(missionId, out DungeonMission? missionRecord)) {
                continue;
            }

            missionRecord.Complete();
            player.Session.Send(DungeonMissionPacket.Update(missionRecord));
        }
    }

    public void DungeonMoveLapTimeToNow(int id) {
        DebugLog("[DungeonMoveLapTimeToNow] id:{Id}", id);
        Field.RoomTimer?.MoveLapToNow(id);
    }

    public void DungeonResetTime(int seconds) {
        DebugLog("[DungeonResetTime] seconds:{Seconds}", seconds);
        Field.RoomTimer?.Restart(seconds * 1000);
    }

    public void DungeonSetEndTime() {
        DebugLog("[DungeonSetEndTime]");
        Field.RoomTimer?.End();
    }

    public void DungeonSetLapTime(int id, int lapTime) {
        DebugLog("[DungeonSetLapTime] id:{Id}, lapTime:{LapTime}", id, lapTime);
        Field.RoomTimer?.SetLap(id, lapTime);
    }

    public void DungeonStopTimer() {
        DebugLog("[DungeonStopTimer]");
        Field.RoomTimer?.Stop();
    }

    public void RandomAdditionalEffect(
        string target,
        int boxId,
        int spawnId,
        int targetCount,
        int tick,
        int waitTick,
        string targetEffect,
        int additionalEffectId
    ) {
        DebugLog("[RandomAdditionalEffect] target:{Target}, boxId:{BoxId}, spawnId:{SpawnId}, targetCount:{Count}, tick:{Tick}, waitTick:{WaitTick}, targetEffect:{Effect}, additionalEffectId:{Id}",
            target, boxId, spawnId, targetCount, tick, waitTick, targetEffect, additionalEffectId);

        // The client shows the effect over one candidate at a time (command 23), then over the chosen
        // ones together (command 24), and removes it on command 25.
        IActor[] candidates = spawnId != 0
            ? Field.Mobs.Values.Where(mob => mob.SpawnPointId == spawnId).ToArray<IActor>()
            : PlayersInBox(boxId).ToArray<IActor>();
        if (candidates.Length == 0 || targetCount <= 0) {
            return;
        }

        Field.Broadcast(TriggerPacket.Effect1(candidates.Select(actor => actor.ObjectId).ToArray(), targetEffect));
        Events.Schedule(() => {
            IActor[] chosen = candidates.Where(actor => !actor.IsDead && Field.TryGetActor(actor.ObjectId, out _))
                .OrderBy(_ => Random.Shared.Next())
                .Take(targetCount)
                .ToArray();
            Field.Broadcast(TriggerPacket.Effect2(chosen.Select(actor => actor.ObjectId).ToArray(), targetEffect));
            Events.Schedule(() => {
                foreach (IActor actor in chosen) {
                    if (additionalEffectId != 0 && !actor.IsDead && Field.TryGetActor(actor.ObjectId, out _)) {
                        actor.Buffs.AddBuff(Field.FieldActor, actor, additionalEffectId, 1, Field.FieldTick);
                    }
                }
                Field.Broadcast(TriggerPacket.EffectStop());
            }, TimeSpan.FromMilliseconds(Math.Max(waitTick, 0)));
        }, TimeSpan.FromMilliseconds(Math.Max(tick, 0)));
    }

    public void SetDungeonVariable(int varId, int value) {
        DebugLog("[SetDungeonVariable] varId:{VarId}, value:{Value}", varId, value);
        DungeonVariables[varId] = value;
    }

    // The Colosseum's continue-play: ClearRound gets the rounds the player has cleared in the week (the room's
    // record keeps its rounds as its reward count), and the next round starts. The room has one player.
    public void SetUserValueFromDungeonRewardCount(string key, int dungeonRewardId) {
        DebugLog("[SetUserValueFromDungeonRewardCount] key:{Key}, dungeonRewardId:{RewardId}", key, dungeonRewardId);
        FieldPlayer? player = Field.Players.Values.FirstOrDefault();
        Field.UserValues.Set(TriggerId, key, player?.Session.Dungeon.RewardCount(dungeonRewardId) ?? 0);
    }

    public void StartTutorial() {
        ErrorLog("[StartTutorial]");
    }

    #region DarkStream
    // A Dark Stream run (see DarkStreamGame): start_game opens the score dialog, a cleared spawn sends the new
    // score, and clearing the last round gives each player in the field the reward row of the rounds cleared
    // (table/darkstream.xml: the exp, the meso, the habi and the items, an item to the bag or else by mail) and
    // sends it in the result with the player's best score before the run (0 before a first run). The run's score
    // is then kept as the player's best when it is higher (DarkStreamBest.Replaces).
    public void DarkStreamSpawnMonster(int[] spawnIds, int score) {
        DebugLog("[DarkStreamSpawnMonster] spawnIds:{SpawnIds}, score:{Score}", string.Join(", ", spawnIds), score);
        foreach (int spawnId in spawnIds) {
            Field.DarkStream?.SetSpawnScore(spawnId, score);
            SpawnNpc(spawnId);
        }
    }

    public void DarkStreamStartGame(int round) {
        DebugLog("[DarkStreamStartGame] round:{Round}", round);
        Field.DarkStream = new DarkStreamGame(round);
        Field.Broadcast(DarkStreamPacket.Open());
    }

    public void DarkStreamStartRound(int round, int uiDuration, int damagePenalty) {
        DebugLog("[DarkStreamStartRound] round:{Round}, uiDuration:{UiDuration}, damagePenalty:{DamagePenalty}", round, uiDuration, damagePenalty);
        Field.DarkStream?.StartRound(round, damagePenalty);
    }

    public void DarkStreamClearRound(int round) {
        DebugLog("[DarkStreamClearRound] round:{Round}", round);
        if (Field.DarkStream is not { } game || !game.ClearRound(round)) {
            return;
        }
        long roomExp = Field is DungeonFieldManager dungeonField ? dungeonField.DungeonMetadata.Reward.Exp : 0;
        DarkStreamGame.Reward reward = DarkStreamGame.RewardOf(Field.TableMetadata.DarkStreamTable.Entries, game.RoundsCleared, roomExp)
            ?? new DarkStreamGame.Reward(0, 0, 0, []);
        long now = DateTime.Now.ToEpochSeconds();
        foreach (FieldPlayer player in Field.Players.Values) {
            GiveDarkStreamReward(player, reward);
            DarkStreamBest? best;
            using (GameStorage.Request db = Field.GameStorage.Context()) {
                best = db.GetDarkStreamBest(player.Value.Character.Id);
                if (DarkStreamBest.Replaces(best, game.Score)) {
                    db.SaveDarkStreamScore(player.Value.Character.Id, game.Score, now);
                }
            }
            player.Session.Send(DarkStreamPacket.Result(game.Score, best?.Score ?? 0, reward.Exp, reward.Meso, reward.Habi, reward.Items));
        }
    }

    private void GiveDarkStreamReward(FieldPlayer player, DarkStreamGame.Reward reward) {
        player.Session.Exp.AddExp(reward.Exp);
        if (reward.Meso > 0) {
            player.Session.Currency.Meso += reward.Meso;
        }
        if (reward.Habi > 0) {
            player.Session.Currency[CurrencyType.HaviFruit] += reward.Habi;
        }
        foreach (RewardItem rewardItem in reward.Items) {
            Item? item = Field.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                continue;
            }
            if (!player.Session.Item.Inventory.Add(item, true)) {
                player.Session.Item.MailItem(item);
            }
        }
    }
    #endregion

    #region ShadowExpedition
    // The boss gauge of the Shadow Expedition fields: the score board 0xE1 opens as the boss gauge with the
    // script's title and most points, at 0 points; close_boss_gauge closes it. What raises the points is not
    // given by the scripts, the client or the tables, so the gauge stays at 0.
    public void ShadowExpeditionOpenBossGauge(int maxGaugePoint, string title) {
        DebugLog("[ShadowExpeditionOpenBossGauge] maxGaugePoint:{Max}, title:{Title}", maxGaugePoint, title);
        Field.Broadcast(ShadowExpeditionPacket.OpenBossGauge(title, 0, maxGaugePoint));
    }

    public void ShadowExpeditionCloseBossGauge() {
        DebugLog("[ShadowExpeditionCloseBossGauge]");
        Field.Broadcast(ShadowExpeditionPacket.Close());
    }
    #endregion

    #region Conditions
    public bool CheckDungeonLobbyUserCount(bool negate) {
        DebugLog("[CheckDungeonLobbyUserCount]");
        if (Field is not DungeonFieldManager dungeonField) {
            return negate;
        }

        if (negate) {
            return Field.Players.Values.Count < dungeonField.Size;
        }
        return Field.Players.Values.Count >= dungeonField.Size;
    }

    // The room's time ran out and was not ended. Without a room timer it stays false.
    public bool DungeonTimeout() {
        DebugLog("[DungeonTimeout]");
        return Field.RoomTimer?.TimedOut(Field.FieldTick) ?? false;
    }

    public bool IsDungeonRoom(bool negate) {
        DebugLog("[IsDungeonRoom]");
        if (negate) {
            return Field is not DungeonFieldManager;
        }
        return Field is DungeonFieldManager;
    }

    // The server runs no Maple Survival match, so no field is playing one.
    public bool IsPlayingMapleSurvival(bool negate) {
        DebugLog("[IsPlayingMapleSurvival] negate:{Negate}", negate);
        return MapleSurvivalResult(negate);
    }

    public static bool MapleSurvivalResult(bool negate) => negate;
    #endregion
}

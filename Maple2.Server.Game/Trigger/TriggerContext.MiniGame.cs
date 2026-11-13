
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Locale = Maple2.Server.Game.Trigger.Helpers.Locale;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    // The players in the winner box when the game ends cleared it: minigame_clear with the field as the code
    // (23400008 names 61000006). KMS2 sends the update when the player reaches the goal, just before the winner
    // buff the script adds after this action.
    internal static void ReportClear(GameSession session, int mapId) {
        session.ConditionUpdate(ConditionType.minigame_clear, codeLong: mapId);
    }

    // The winners also get the items of the running MiniGameBonusReward events that name the game (MiniGameBonus).
    public void EndMiniGame(int winnerBoxId, string gameName, bool isOnlyWinner) {
        IEnumerable<FieldPlayer> players = PlayersInBox(winnerBoxId);
        foreach (FieldPlayer player in players) {
            if (player.Session.MiniGameRecord == null) {
                continue;
            }

            ReportClear(player.Session, Field.MapId);
            MiniGameBonus.Give(player.Session, player.Session.MiniGameRecord, gameName);
            if (player.Session.MiniGameRecord.ShowResult) {
                player.Session.Send(DungeonRewardPacket.MiniGame(player.Session.MiniGameRecord));
            }
            player.Session.MiniGameRecord = null;
        }

        if (isOnlyWinner) {
            return;
        }

        IEnumerable<FieldPlayer> losingPlayers = PlayersNotInBox(winnerBoxId);
        foreach (FieldPlayer player in losingPlayers) {
            if (player.Session.MiniGameRecord != null) {
                if (player.Session.MiniGameRecord.ShowResult) {
                    player.Session.Send(DungeonRewardPacket.MiniGame(player.Session.MiniGameRecord));
                }
                player.Session.MiniGameRecord = null;
            }
        }
    }

    public void EndMiniGameRound(int winnerBoxId, float expRate, float meso, bool isOnlyWinner, bool isGainLoserBonus, string gameName) {
        ExpType expType = MiniGameClearRules.IsUserOpenGame(gameName) ? ExpType.userMiniGame : ExpType.miniGame;
        IEnumerable<FieldPlayer> players = PlayersInBox(winnerBoxId);
        foreach (FieldPlayer player in players) {
            MiniGameUserRecord? record = player.Session.MiniGameRecord;
            if (record == null) {
                continue;
            }

            long totalExp = player.Session.Exp.AddExp(expType, expRate);
            record.Rewards[DungeonRewardType.Exp] += (int) totalExp;

            if (meso > 0.0f) {
                player.Session.Currency.Meso += (long) meso;
                record.Rewards[DungeonRewardType.Meso] += (int) meso;
            }
            record.ClearedRounds++;
        }
        if (isOnlyWinner) {
            return;
        }

        IEnumerable<FieldPlayer> losingPlayers = PlayersNotInBox(winnerBoxId);
        foreach (FieldPlayer player in losingPlayers) {
            if (player.Session.MiniGameRecord != null) {
                if (isGainLoserBonus) {
                    long totalExp = player.Session.Exp.AddExp(ExpType.miniGame, expRate);
                    player.Session.MiniGameRecord.Rewards[DungeonRewardType.Exp] += (int) totalExp;
                }
                if (player.Session.MiniGameRecord.ShowResult) {
                    player.Session.Send(DungeonRewardPacket.MiniGame(player.Session.MiniGameRecord));
                }
                player.Session.MiniGameRecord = null;
            }
        }
    }

    public void MiniGameCameraDirection(int boxId, int cameraId) {
        IEnumerable<FieldPlayer> players = PlayersInBox(boxId);
        foreach (FieldPlayer player in players) {
            player.Session.Send(CameraPacket.Local(cameraId, true));
        }
    }

    public void MiniGameGiveExp(int boxId, float expRate, bool isOutSide) {
        IEnumerable<FieldPlayer> players = PlayersInBox(boxId);
        foreach (FieldPlayer player in players) {
            if (player.Session.MiniGameRecord == null) {
                continue;
            }

            long totalExp = player.Session.Exp.AddExp(ExpType.miniGame, expRate);
            player.Session.MiniGameRecord.Rewards[DungeonRewardType.Exp] += (int) totalExp;
        }
    }

    public void MiniGameGiveReward(int winnerBoxId, string contentType, string type) {
        if (!Constant.ContentRewards.TryGetValue(contentType, out int rewardId)) {
            WarnLog("MiniGameGiveReward: {Type} not found", type);
            return;
        }
        IEnumerable<FieldPlayer> players = PlayersInBox(winnerBoxId);
        foreach (FieldPlayer player in players) {
            // Only a player who was in the box at the game's start has a record, and leaving the field drops it, so
            // a player who comes back in is not paid; the record's flag pays a game once if the action runs again.
            MiniGameUserRecord? record = player.Session.MiniGameRecord;
            if (record == null || record.RewardGiven) {
                continue;
            }
            record.RewardGiven = true;

            if (!MiniGameClearRules.CountsAsUserOpenClear(contentType)) {
                record.Add(player.Session.GetRewardContent(rewardId));
                continue;
            }
            player.Session.ConditionUpdate(ConditionType.useropen_minigame_clear, codeLong: Field.MapId);
            GiveExtraReward(player.Session, record, rewardId);
        }
    }

    // The user-opened games' extra reward, userMassiveExtraRewardMax times a day; past that the clear pays nothing
    // more. The count is stored before the reward is given, so a failure cannot pay twice.
    private static void GiveExtraReward(GameSession session, MiniGameUserRecord record, int rewardId) {
        Character character = session.Player.Value.Character;
        int maxPerDay = session.ServerTableMetadata.ConstantsTable.userMassiveExtraRewardMax;
        int paidToday = PetFusionRules.DailyCount(character.MassiveExtraRewardCount, character.MassiveExtraRewardTime,
            PetFusionRules.LastResetTime());
        if (!MiniGameClearRules.CanPayExtraReward(paidToday, maxPerDay)) {
            return;
        }

        character.MassiveExtraRewardCount = paidToday + 1;
        character.MassiveExtraRewardTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        record.Add(session.GetRewardContent(rewardId));
        (int a, int b) = MiniGameClearRules.NoticeArgs(paidToday + 1, maxPerDay);
        session.Send(PlayerHostPacket.MiniGameRewardReceive(a, b));
    }

    // Chosen here: the start notice goes to each player of a user-opened game when it starts, while the day still has
    // extra rewards left (the client shows it on mode 4 alone; no capture says when KMS2 sent it).
    private static void NoticeExtraReward(GameSession session) {
        Character character = session.Player.Value.Character;
        int maxPerDay = session.ServerTableMetadata.ConstantsTable.userMassiveExtraRewardMax;
        int paidToday = PetFusionRules.DailyCount(character.MassiveExtraRewardCount, character.MassiveExtraRewardTime,
            PetFusionRules.LastResetTime());
        if (!MiniGameClearRules.CanPayExtraReward(paidToday, maxPerDay)) {
            return;
        }

        (int a, int b) = MiniGameClearRules.NoticeArgs(paidToday, maxPerDay);
        session.Send(PlayerHostPacket.MiniGameRewardNotice(a, b));
    }

    public void SetMiniGameAreaForHack(int boxId) { }

    public void StartMiniGame(int boxId, int round, string type, bool isShowResultUi) {
        IEnumerable<FieldPlayer> players = PlayersInBox(boxId);

        foreach (FieldPlayer player in players) {
            player.Session.MiniGameRecord = new MiniGameUserRecord(player.Session.CharacterId) {
                TotalRounds = round,
                ShowResult = isShowResultUi,
                MinRound = 1,
            };
            if (MiniGameClearRules.IsUserOpenGame(type)) {
                NoticeExtraReward(player.Session);
            }
        }
    }

    // KMS2 sends nothing for the round start: the round UI comes from set_event_ui_round alone, which the
    // scripts that show one call themselves. A second one here replayed the round banner a few seconds later.
    public void StartMiniGameRound(int boxId, int round) { }

    public void UnsetMiniGameAreaForHack() { }

    public void UseState(int id, bool randomize) {
        if (!Field.States.TryGetValue(id, out List<TriggerState>? states)) {
            return;
        }

        if (randomize) {
            // randomize order
            states = states.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        // get first state and remove from list
        TriggerState state = states.First();
        states.RemoveAt(0);
        Field.States[id] = states;

        // They only have OnEnter() method
        state?.OnEnter();
    }

    #region CathyMart
    // The client (WorldService::AddEffect, worldservice_1.cpp) keys the effect by the object id it names and
    // draws the nif on that entity; the script names the npc by its spawn point.
    public void AddEffectNif(int spawnPointId, string nifPath, bool isOutline, float scale, int rotateZ) {
        foreach (FieldNpc npc in Field.EnumerateNpcs().Where(npc => npc.SpawnPointId == spawnPointId)) {
            Field.NpcEffects[npc.ObjectId] = (nifPath, isOutline, scale, rotateZ);
            Broadcast(TriggerPacket.AddEffect(npc.ObjectId, nifPath, isOutline, scale, rotateZ));
        }
    }

    public void RemoveEffectNif(int spawnPointId) {
        foreach (FieldNpc npc in Field.EnumerateNpcs().Where(npc => npc.SpawnPointId == spawnPointId)) {
            Field.NpcEffects.TryRemove(npc.ObjectId, out _);
            Broadcast(TriggerPacket.RemoveEffect(npc.ObjectId));
        }
    }
    #endregion

    #region HideAndSeek
    // The field games: MapleSurvival (SurvivalGameField), HideAndSeek (HideAndSeekField) and GuildVsGame
    // (GuildVsGameField, made at the map's first arrival too); the others are not built.
    public void CreateFieldGame(FieldGame type, bool reset) {
        if (type == FieldGame.GuildVsGame) {
            Field.StartGuildVsGame();
        }
        if (type == FieldGame.MapleSurvival && Field.Survival is null or { Ended: true }) {
            Field.StartSurvival();
        }
        // The game sets its steps' user values on this trigger (61000023_main waits for them).
        if (type == FieldGame.HideAndSeek && (reset || Field.HideAndSeek is null or { Ended: true })) {
            Field.StartHideAndSeek(TriggerId);
        }
    }

    // The constants of the field's Hide and Seek game; a value of another region is skipped (the ingest already
    // dropped the ones of a disabled feature).
    public void FieldGameConstant(string key, string value, string feature, Locale locale) {
        DebugLog("[FieldGameConstant] key:{Key}, value:{Value}, locale:{Locale}", key, value, locale);
        Field.HideAndSeek?.Constant(key, value, locale.ToString());
    }

    // A banner to one team of the Hide and Seek game (custom 1 the hiders, 2 the seekers); SetEventUI is the only type.
    public void FieldGameMessage(int custom, string type, bool arg1, string script, int duration) {
        DebugLog("[FieldGameMessage] custom:{Custom}, type:{Type}, script:{Script}, duration:{Duration}", custom, type, script, duration);
        if (type == "SetEventUI") {
            Field.HideAndSeek?.Message(custom, script, duration);
        }
    }
    #endregion

    #region Conditions
    public bool BonusGameReward(int boxId, int type) {
        return -1 == type;
    }
    #endregion
}

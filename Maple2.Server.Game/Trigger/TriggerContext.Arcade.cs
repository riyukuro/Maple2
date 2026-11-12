using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    #region BoomBoomOcean
    // The Boom Boom Ocean game (BoomBoomOceanGame, FieldManager.Arcade): start_game opens the score dialog and the
    // life bar, a scored skill's hit or buff on a game player sends the new score (a negative one takes a life),
    // clear_round adds the round's time score, and end_game gives the reward and the result.
    public void ArcadeBoomBoomOceanSetSkillScore(int skillId, int score) {
        DebugLog("[ArcadeBoomBoomOceanSetSkillScore] skillId:{Id}, score:{Score}", skillId, score);
        Field.BoomBoomOcean?.SetSkillScore(skillId, score);
    }

    public void ArcadeBoomBoomOceanStartGame(int lifeCount) {
        DebugLog("[ArcadeBoomBoomOceanStartGame] lifeCount:{LifeCount}", lifeCount);
        var game = new BoomBoomOceanGame(lifeCount, Field.Players.Values.Select(player => player.Value.Character.Id));
        Field.BoomBoomOcean = game;
        Field.Broadcast(ArcadePacket.Open(game.Id));
        Field.Broadcast(ArcadePacket.LifeInit(1, game.Lives, game.LifeCount));
    }

    public void ArcadeBoomBoomOceanEndGame() {
        DebugLog("[ArcadeBoomBoomOceanEndGame]");
        Field.EndBoomBoomOcean();
    }

    public void ArcadeBoomBoomOceanStartRound(int round, int roundDuration, int timeScoreRate) {
        DebugLog("[ArcadeBoomBoomOceanStartRound] round:{Round}, roundDuration:{Duration}, timeScoreRate:{Rate}", round, roundDuration, timeScoreRate);
        Field.BoomBoomOcean?.StartRound(round, roundDuration, timeScoreRate, Field.FieldTick);
    }

    public void ArcadeBoomBoomOceanClearRound(int round) {
        DebugLog("[ArcadeBoomBoomOceanClearRound] round:{Round}", round);
        if (Field.BoomBoomOcean is { } game && game.ClearRound(round, Field.FieldTick) > 0) {
            Field.Broadcast(ArcadePacket.Score(game.Score));
        }
    }
    #endregion

    #region SpringFarm
    // The Spring Farm game (SpringFarmGame, FieldManager.Arcade): start_game opens the score dialog and the life
    // bar, a used interact object or a cleared spawn sends the new score, clear_round adds the time score, and
    // end_game gives the reward and the result.
    public void ArcadeSpringFarmSetInteractScore(int interactId, int score) {
        DebugLog("[ArcadeSpringFarmSetInteractScore] interactId:{Id}, score:{Score}", interactId, score);
        Field.SpringFarm?.SetInteractScore(interactId, score);
    }

    public void ArcadeSpringFarmSpawnMonster(int[] spawnIds, int score) {
        DebugLog("[ArcadeSpringFarmSpawnMonster] spawnIds:{SpawnIds}, score:{Score}", string.Join(", ", spawnIds), score);
        foreach (int spawnId in spawnIds) {
            Field.SpringFarm?.SetSpawnScore(spawnId, score);
            SpawnNpc(spawnId);
        }
    }

    // The open type is the arcade id: the client places the score dialog apart only for type 3. The players in the
    // field now are the game's (field 51000001 is a solo instance field, one field per entry).
    public void ArcadeSpringFarmStartGame(int lifeCount) {
        DebugLog("[ArcadeSpringFarmStartGame] lifeCount:{LifeCount}", lifeCount);
        var game = new SpringFarmGame(lifeCount, Field.Players.Values.Select(player => player.Value.Character.Id));
        Field.SpringFarm = game;
        Field.Broadcast(ArcadePacket.Open(SpringFarmGame.ArcadeId));
        Field.Broadcast(ArcadePacket.LifeInit(1, game.Lives, game.LifeCount));
    }

    public void ArcadeSpringFarmEndGame() {
        DebugLog("[ArcadeSpringFarmEndGame]");
        Field.EndSpringFarm();
    }

    public void ArcadeSpringFarmStartRound(int round, int uiDuration, string timeScoreType, int timeScoreRate, int roundDuration) {
        DebugLog("[ArcadeSpringFarmStartRound] round:{Round}, uiDuration:{UiDuration}, timeScoreType:{Type}, timeScoreRate:{Rate}, roundDuration:{Duration}",
            round, uiDuration, timeScoreType, timeScoreRate, roundDuration);
        Field.SpringFarm?.StartRound(round, timeScoreType, timeScoreRate, roundDuration, Field.FieldTick);
    }

    public void ArcadeSpringFarmClearRound(int round) {
        DebugLog("[ArcadeSpringFarmClearRound] round:{Round}", round);
        if (Field.SpringFarm?.ClearRound(round, Field.FieldTick) > 0) {
            Field.SpringFarmScoreChanged();
        }
    }
    #endregion

    #region ThreeTwoOne
    // The Three Two One games of arcades 3, 4 and 5 (ThreeTwoOneGame, FieldManager.Arcade): start_game opens the
    // score dialog at the starting score and the life bar, start_round shows the direction buttons for ui_duration
    // seconds, result_round hides them and sets the script's ThreeTwoOneResult user value (1: the players won),
    // result_round2 adds the won round's score or takes a life and shows the wins in a row, and end_game gives the
    // reward and the result.
    public const string ThreeTwoOneResultKey = "ThreeTwoOneResult";

    private void ThreeTwoOneStartGame(int arcadeId, int lifeCount, int initScore) {
        DebugLog("[ArcadeThreeTwoOneStartGame] arcadeId:{ArcadeId}, lifeCount:{LifeCount}, initScore:{InitScore}", arcadeId, lifeCount, initScore);
        var game = new ThreeTwoOneGame(arcadeId, lifeCount, initScore, Field.Players.Values.Select(player => player.Value.Character.Id));
        Field.ThreeTwoOne = game;
        Field.Broadcast(ArcadePacket.Open(game.Id));
        Field.Broadcast(ArcadePacket.Score(game.Score));
        Field.Broadcast(ArcadePacket.LifeInit(1, game.Lives, game.LifeCount));
    }

    private void ThreeTwoOneEndGame() {
        DebugLog("[ArcadeThreeTwoOneEndGame]");
        Field.EndThreeTwoOne();
    }

    private void ThreeTwoOneStartRound(int round, int uiDuration) {
        DebugLog("[ArcadeThreeTwoOneStartRound] round:{Round}, uiDuration:{UiDuration}", round, uiDuration);
        if (Field.ThreeTwoOne is not { Ended: false } game) {
            return;
        }
        game.StartRound(round);
        Field.Broadcast(ArcadePacket.Buttons(true, uiDuration));
    }

    private void ThreeTwoOneResultRound(int resultDirection) {
        DebugLog("[ArcadeThreeTwoOneResultRound] resultDirection:{Direction}", resultDirection);
        if (Field.ThreeTwoOne is not { Ended: false }) {
            return;
        }
        bool win = Field.ThreeTwoOneResult(resultDirection);
        Field.Broadcast(ArcadePacket.Buttons(false));
        Field.UserValues.Set(TriggerId, ThreeTwoOneResultKey, win ? 1 : 0);
    }

    private void ThreeTwoOneResultRound2(int round) {
        DebugLog("[ArcadeThreeTwoOneResultRound2] round:{Round}", round);
        Field.ThreeTwoOneResult2();
    }

    private void ThreeTwoOneClearRound(int round) {
        DebugLog("[ArcadeThreeTwoOneClearRound] round:{Round}", round);
        Field.ThreeTwoOne?.ClearRound(round);
    }

    public void ArcadeThreeTwoOneStartGame(int lifeCount, int initScore) => ThreeTwoOneStartGame(3, lifeCount, initScore);

    public void ArcadeThreeTwoOneEndGame() => ThreeTwoOneEndGame();

    public void ArcadeThreeTwoOneStartRound(int round, int uiDuration) => ThreeTwoOneStartRound(round, uiDuration);

    public void ArcadeThreeTwoOneResultRound(int resultDirection) => ThreeTwoOneResultRound(resultDirection);

    public void ArcadeThreeTwoOneResultRound2(int round) => ThreeTwoOneResultRound2(round);

    public void ArcadeThreeTwoOneClearRound(int round) => ThreeTwoOneClearRound(round);

    public void ArcadeThreeTwoOne2StartGame(int lifeCount, int initScore) => ThreeTwoOneStartGame(4, lifeCount, initScore);

    public void ArcadeThreeTwoOne2EndGame() => ThreeTwoOneEndGame();

    public void ArcadeThreeTwoOne2StartRound(int round, int uiDuration) => ThreeTwoOneStartRound(round, uiDuration);

    public void ArcadeThreeTwoOne2ResultRound(int resultDirection) => ThreeTwoOneResultRound(resultDirection);

    public void ArcadeThreeTwoOne2ResultRound2(int round) => ThreeTwoOneResultRound2(round);

    public void ArcadeThreeTwoOne2ClearRound(int round) => ThreeTwoOneClearRound(round);

    public void ArcadeThreeTwoOne3StartGame(int lifeCount, int initScore) => ThreeTwoOneStartGame(5, lifeCount, initScore);

    public void ArcadeThreeTwoOne3EndGame() => ThreeTwoOneEndGame();

    public void ArcadeThreeTwoOne3StartRound(int round, int uiDuration) => ThreeTwoOneStartRound(round, uiDuration);

    public void ArcadeThreeTwoOne3ResultRound(int resultDirection) => ThreeTwoOneResultRound(resultDirection);

    public void ArcadeThreeTwoOne3ResultRound2(int round) => ThreeTwoOneResultRound2(round);

    public void ArcadeThreeTwoOne3ClearRound(int round) => ThreeTwoOneClearRound(round);
    #endregion
}

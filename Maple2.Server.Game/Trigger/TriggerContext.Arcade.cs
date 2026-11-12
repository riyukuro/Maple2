using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    #region BoomBoomOcean
    public void ArcadeBoomBoomOceanSetSkillScore(int skillId, int score) { }

    public void ArcadeBoomBoomOceanStartGame(int lifeCount) { }

    public void ArcadeBoomBoomOceanEndGame() { }

    public void ArcadeBoomBoomOceanStartRound(int round, int roundDuration, int timeScoreRate) { }

    public void ArcadeBoomBoomOceanClearRound(int round) { }
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

    // The open type is the arcade id: the client places the score dialog apart only for type 3.
    public void ArcadeSpringFarmStartGame(int lifeCount) {
        DebugLog("[ArcadeSpringFarmStartGame] lifeCount:{LifeCount}", lifeCount);
        var game = new SpringFarmGame(lifeCount);
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
    public void ArcadeThreeTwoOneStartGame(int lifeCount, int initScore) { }

    public void ArcadeThreeTwoOneEndGame() { }

    public void ArcadeThreeTwoOneStartRound(int round, int uiDuration) { }

    public void ArcadeThreeTwoOneResultRound(int resultDirection) { }

    public void ArcadeThreeTwoOneResultRound2(int round) { }

    public void ArcadeThreeTwoOneClearRound(int round) { }

    public void ArcadeThreeTwoOne2StartGame(int lifeCount, int initScore) { }

    public void ArcadeThreeTwoOne2EndGame() { }

    public void ArcadeThreeTwoOne2StartRound(int round, int uiDuration) { }

    public void ArcadeThreeTwoOne2ResultRound(int resultDirection) { }

    public void ArcadeThreeTwoOne2ResultRound2(int round) { }

    public void ArcadeThreeTwoOne2ClearRound(int round) { }

    public void ArcadeThreeTwoOne3StartGame(int lifeCount, int initScore) { }

    public void ArcadeThreeTwoOne3EndGame() { }

    public void ArcadeThreeTwoOne3StartRound(int round, int uiDuration) { }

    public void ArcadeThreeTwoOne3ResultRound(int resultDirection) { }

    public void ArcadeThreeTwoOne3ResultRound2(int round) { }

    public void ArcadeThreeTwoOne3ClearRound(int round) { }
    #endregion
}

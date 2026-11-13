
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void GiveGuildExp(int boxId, int type) {
        DebugLog("[GiveGuildExp] boxId:{BoxId}, type:{Type}", boxId, type);
        InstanceType? fieldType = Field.ServerTableMetadata.InstanceFieldTable.Entries.TryGetValue(Field.MapId, out InstanceFieldMetadata? instance)
            ? instance.Type : null;
        GiveGuildExp(PlayersInBox(boxId).Select(player => player.Session), type, fieldType);
    }

    // Each guild with a member among the players gains once (GuildRules.GainExpFund at the guild's level and the
    // field's instance type), asked
    // for by its first member in the list; the World adds it and tells every member (Guild 53 and 97).
    public static void GiveGuildExp(IEnumerable<GameSession> sessions, int type, InstanceType? fieldType) {
        var paid = new HashSet<long>();
        foreach (GameSession session in sessions) {
            Guild? guild = session.Guild.Guild;
            if (guild == null || !paid.Add(guild.Id)) {
                continue;
            }
            GuildTable.Property? property = GuildRules.PropertyAt(session.TableMetadata.GuildTable.Properties, guild.Experience);
            (int exp, int fund) = GuildRules.GainExpFund(property, type, fieldType);
            session.Guild.AddExpFund(exp, fund);
        }
    }

    #region GuildVsGame
    // The guild vs game of 66200001_gd (GuildVsGameField). Each action is a no-op on a field without the game.
    public void GuildVsGameEndGame() {
        DebugLog("[GuildVsGameEndGame]");
        Field.GuildVsGame?.End();
    }

    public void GuildVsGameGiveContribution(int teamId, bool isWin, string description) {
        DebugLog("[GuildVsGameGiveContribution] teamId:{TeamId}, isWin:{IsWin}", teamId, isWin);
        Field.GuildVsGame?.GiveContribution(teamId, isWin);
    }

    public void GuildVsGameGiveReward(string type, int teamId, bool isWin, string description) {
        DebugLog("[GuildVsGameGiveReward] type:{Type}, teamId:{TeamId}, isWin:{IsWin}", type, teamId, isWin);
        Field.GuildVsGame?.GiveReward(type, teamId, isWin);
    }

    public void GuildVsGameLogResult(string description) {
        Field.GuildVsGame?.LogResult();
    }

    public void GuildVsGameLogWonByDefault(int teamId, string description) {
        Field.GuildVsGame?.LogWonByDefault(teamId);
    }

    public void GuildVsGameResult(string description) {
        DebugLog("[GuildVsGameResult]");
        Field.GuildVsGame?.Result();
    }

    public void GuildVsGameScoreByUser(int boxId, int score, string description) {
        DebugLog("[GuildVsGameScoreByUser] boxId:{BoxId}, score:{Score}", boxId, score);
        if (Objects.Boxes.TryGetValue(boxId, out TriggerBox? box)) {
            Field.GuildVsGame?.ScoreByUser(box, score);
        }
    }

    public void SetUserValueFromGuildVsGameScore(int teamId, string key) {
        DebugLog("[SetUserValueFromGuildVsGameScore] teamId:{TeamId}, key:{Key}", teamId, key);
        Field.UserValues.Set(TriggerId, key, Field.GuildVsGame?.State.Score(teamId) ?? 0);
    }
    #endregion

    // The players in the box with the user tag (as CountUsers counts them; tag 0 counts every player), as this
    // trigger's user value.
    public void SetUserValueFromUserCount(int boxId, string key, int userTagId) {
        DebugLog("[SetUserValueFromUserCount] boxId:{BoxId}, key:{Key}, userTagId:{TagId}", boxId, key, userTagId);
        int count = 0;
        if (Objects.Boxes.TryGetValue(boxId, out TriggerBox? box)) {
            count = Field.Players.Values.Count(player => (userTagId <= 0 || player.TagId == userTagId) && box.Contains(player.Position));
        }
        Field.UserValues.Set(TriggerId, key, count);
    }

    // Shows this trigger's user value on digit meshes: digit d of position p (0 the ones) is mesh start + 10p + d,
    // and the other nine meshes of the position are hidden.
    public void UserValueToNumberMesh(string key, int startMeshId, int digitCount) {
        DebugLog("[UserValueToNumberMesh] key:{Key}, startMeshId:{Start}, digitCount:{Count}", key, startMeshId, digitCount);
        (int[] shown, int[] hidden) = NumberMeshes(Field.UserValues.Get(TriggerId, key), startMeshId, digitCount);
        UpdateMesh(hidden.Where(Objects.Meshes.ContainsKey).ToArray(), false, 0, 0);
        UpdateMesh(shown.Where(Objects.Meshes.ContainsKey).ToArray(), true, 0, 0);
    }

    // Chosen here: the order of the digit blocks. Each block is the ten meshes of digits 0-9 (one model for 0 in every
    // block); the block at start + 10 always stands left of the one at start (66200001_gd's xblock), so start is the
    // ones and start + 10 the tens. Every position shows a digit, a leading 0 too; a value past the digits keeps its
    // low digits, and a negative one shows 0.
    public static (int[] Shown, int[] Hidden) NumberMeshes(int value, int startMeshId, int digitCount) {
        var shown = new List<int>();
        var hidden = new List<int>();
        int rest = Math.Max(0, value);
        for (int position = 0; position < digitCount; position++) {
            int digit = rest % 10;
            rest /= 10;
            for (int d = 0; d < 10; d++) {
                (d == digit ? shown : hidden).Add(startMeshId + 10 * position + d);
            }
        }
        return (shown.ToArray(), hidden.ToArray());
    }

    #region Conditions
    public bool GuildVsGameScoredTeam(int teamId) {
        return Field.GuildVsGame?.State.LastScorer == teamId;
    }

    public bool GuildVsGameWinnerTeam(int teamId) {
        return Field.GuildVsGame is { } game && game.State.Winner == teamId;
    }
    #endregion
}

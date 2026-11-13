
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

    public void GuildVsGameEndGame() { }

    public void GuildVsGameGiveContribution(int teamId, bool isWin, string description) { }

    public void GuildVsGameGiveReward(string type, int teamId, bool isWin, string description) { }

    public void GuildVsGameLogResult(string description) { }

    public void GuildVsGameLogWonByDefault(int teamId, string description) { }

    public void GuildVsGameResult(string description) { }

    public void GuildVsGameScoreByUser(int boxId, int score, string description) { }

    public void SetUserValueFromGuildVsGameScore(int teamId, string key) { }

    public void SetUserValueFromUserCount(int boxId, string key, int userTagId) { }

    public void UserValueToNumberMesh(string key, int startMeshId, int digitCount) { }

    #region Conditions
    public bool GuildVsGameScoredTeam(int teamId) {
        return false;
    }

    public bool GuildVsGameWinnerTeam(int teamId) {
        return false;
    }
    #endregion
}

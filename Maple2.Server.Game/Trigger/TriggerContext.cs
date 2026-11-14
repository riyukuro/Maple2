using Maple2.Server.Game.Util;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BasicAttribute = Maple2.Model.Enum.BasicAttribute;
using DungeonGroupType = Maple2.Model.Enum.DungeonGroupType;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Scheduler;
using Serilog;
using Serilog.Core;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext : ITriggerContext {
    private readonly FieldTrigger owner;
    private readonly ILogger logger = Log.Logger.ForContext<TriggerContext>();

    private FieldManager Field => owner.Field;
    // The trigger model id of this script; its user values are kept under it.
    private int TriggerId => owner.Value.Id;
    private TriggerCollection Objects => owner.Field.TriggerObjects;

    private float currentRandom = float.MaxValue;

    // Skip state class reference, must instantiate before using.
    private TriggerState? skipState;
    public readonly EventQueue Events;
    public long StartTick;

    public TriggerContext(FieldTrigger owner) {
        this.owner = owner;

        Events = new EventQueue(logger);
        Events.Start();
        StartTick = Environment.TickCount64;
    }

    public bool TryGetSkip([NotNullWhen(true)] out TriggerState? state) {
        if (skipState == null) {
            state = null;
            return false;
        }

        state = skipState;
        return true;
    }

    private void Broadcast(ByteWriter packet) => Field.Broadcast(packet);

    private string lastDebugKey = "";

    [Conditional("TRIGGER_DEBUG")]
    [MessageTemplateFormatMethod("messageTemplate")]
    internal void DebugLog(string messageTemplate, params object[] args) {
        LogOnce(logger.Debug, messageTemplate, args);
    }

    [Conditional("TRIGGER_DEBUG")]
    [MessageTemplateFormatMethod("messageTemplate")]
    internal void WarnLog(string messageTemplate, params object[] args) {
        LogOnce(logger.Warning, messageTemplate, args);
    }

    [Conditional("TRIGGER_DEBUG")]
    [MessageTemplateFormatMethod("messageTemplate")]
    internal void ErrorLog(string messageTemplate, params object[] args) {
        LogOnce(logger.Error, messageTemplate, args);
    }

    private void LogOnce(Action<string, object[]> logAction, string messageTemplate, params object[] args) {
        string key = messageTemplate + string.Join(", ", args);
        if (key == lastDebugKey) {
            return;
        }

        logAction($"{owner.Value.Name} {messageTemplate}", args);
        lastDebugKey = key;
    }

    // Accessors
    // The open boss gauge holds at least point points (the kill share, FieldManager.ShadowGauge). With no gauge
    // open it waits for one (52010038_qd tests 150 points before its gauge script opens it), unless no script of
    // the field opens one: then it passes as before (no KR shadow map is such a field).
    public bool ShadowExpeditionPoints(int point) {
        ScoreBoard.Board? board = Field.ScoreBoardOpen;
        int? points = board?.Kind == ShadowExpeditionPacket.BossGauge ? board.Points : null;
        bool reached = ShadowGaugeReached(points, Field.OpensShadowGauge, point);
        DebugLog("[GetShadowExpeditionPoints] point:{Point}, points:{Points}, reached:{Reached}", point, points, reached);
        return reached;
    }

    public static bool ShadowGaugeReached(int? points, bool fieldOpensGauge, int point) {
        return points == null ? !fieldOpensGauge : points >= point;
    }

    // The instance's dungeon variable equals value; an id never set reads 0.
    public bool DungeonVariable(int id, int value) {
        DebugLog("[GetDungeonVariable] id:{Id}, value:{Value}", id, value);
        return DungeonVariableEquals(DungeonVariables, id, value);
    }

    // The store of the dungeon instance the field belongs to, else the field's own.
    private System.Collections.Concurrent.ConcurrentDictionary<int, int> DungeonVariables =>
        Field is DungeonFieldManager dungeonField ? dungeonField.DungeonRoomRecord.Variables : Field.DungeonVariables;

    public static bool DungeonVariableEquals(IReadOnlyDictionary<int, int> variables, int id, int value) {
        return variables.GetValueOrDefault(id, 0) == value;
    }

    // The damage the first npc of the spawn point took so far, over its max hp (1.0 = its whole hp), as the
    // shipped scripts describe it (map 02000410). With no such npc in the field the test fails.
    public bool NpcDamage(int spawnPointId, float damage, OperatorType operatorType) {
        DebugLog("[GetNpcDamageRate] spawnPointId:{Id}, damage:{Damage}, operatorType:{Operator}", spawnPointId, damage, operatorType);
        FieldNpc? npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnPointId);
        if (npc == null) {
            return false;
        }

        long total = npc.Stats.Values[BasicAttribute.Health].Total;
        double rate = total > 0 ? (double) npc.DamageTaken / total : 0;
        return CompareRate(rate, damage, operatorType);
    }

    // In float, as the script's value is: 300 of 1000 hp is a damage_rate of exactly 0.3.
    public static bool CompareRate(double exact, float value, OperatorType operatorType) {
        float rate = (float) exact;
        return operatorType switch {
            OperatorType.Greater => rate > value,
            OperatorType.GreaterEqual => rate >= value,
            OperatorType.Equal => Math.Abs(rate - value) < 0.0001,
            OperatorType.LessEqual => rate <= value,
            OperatorType.Less => rate < value,
            _ => false,
        };
    }

    // The hp of the npc of that spawn point, in percent of its max hp when relative (all shipped uses), else in
    // points. With no such npc in the field the test fails.
    public bool NpcHp(int spawnPointId, bool isRelative, int value, CompareType compareType) {
        DebugLog("[GetNpcHpRate] spawnPointId:{Id}, isRelative:{IsRelative}, value:{Value}, compareType:{CompareType}", spawnPointId, isRelative, value, compareType);
        FieldNpc? npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnPointId);
        if (npc == null) {
            return false;
        }

        long current = Math.Max(0, npc.Stats.Values[BasicAttribute.Health].Current);
        long total = npc.Stats.Values[BasicAttribute.Health].Total;
        return CompareNpcHp(current, total, isRelative, value, compareType);
    }

    public static bool CompareNpcHp(long current, long total, bool isRelative, int value, CompareType compareType) {
        double hp = isRelative ? (total > 0 ? current * 100.0 / total : 0) : current;
        return compareType switch {
            CompareType.lower => hp < value,
            CompareType.lowerEqual => hp <= value,
            CompareType.higher => hp > value,
            CompareType.higherEqual => hp >= value,
            _ => false,
        };
    }

    // The dungeon room id from DungeonRoom.xml; a field that is no dungeon room has 0.
    public bool DungeonId(int dungeonId) {
        DebugLog("[DungeonId] dungeonId:{DungeonId}", dungeonId);
        return DungeonIdMatches(Field.DungeonId, dungeonId);
    }

    public static bool DungeonIdMatches(int fieldDungeonId, int dungeonId) => fieldDungeonId == dungeonId;

    // The scripts test 2 (raid) and 3 (chaos raid): the room's groupType in DungeonRoom.xml, not its
    // dungeonLevel (a character level). Outside a raid room the old answer (chaos) stays, so a script
    // that tests only 2 and 3 still goes on.
    public bool DungeonLevel(int level) {
        DebugLog("[DungeonLevel] level:{Level}", level);
        DungeonGroupType? groupType = (Field as DungeonFieldManager)?.DungeonMetadata.GroupType;
        return DungeonLevelMatches(groupType, level);
    }

    public static bool DungeonLevelMatches(DungeonGroupType? groupType, int level) {
        return groupType is DungeonGroupType.raid or DungeonGroupType.chaosRaid ? (int) groupType == level : level == 3;
    }

    // The room's maximum as the client's DungeonRoom::GetMaxUserCount gives it for the room's user count
    // (the party size it was made with). The shipped scripts test 1 to 4 only (dungeon_common/checkusercount
    // has no other way on in a room), so outside a dungeon room and in a room whose maximum is above 4
    // (the 6 and 10 user rooms) the old answer (1) stays.
    public bool DungeonMaxUserCount(int value) {
        DebugLog("[DungeonMaxUserCount] value:{Value}", value);
        if (Field is not DungeonFieldManager dungeonField) {
            return value == 1;
        }
        DungeonRoomLimitMetadata limit = dungeonField.DungeonMetadata.Limit;
        return DungeonMaxUserCountMatches(RoomMaxUserCount(limit.ChangeMaxUsers, limit.MaxUserCount, dungeonField.Size), value);
    }

    public static bool DungeonMaxUserCountMatches(int roomMaxUserCount, int value) {
        return roomMaxUserCount > 4 ? value == 1 : roomMaxUserCount == value;
    }

    // DungeonRoom::GetMaxUserCount (0x14031e030): the table maximum, or with isChangeMaxUser the user count
    // held between 2 and that maximum.
    public static int RoomMaxUserCount(bool changeMaxUsers, int maxUserCount, int userCount) {
        if (!changeMaxUsers) {
            return maxUserCount;
        }
        if (userCount < 2) {
            return 2;
        }
        return userCount > maxUserCount ? maxUserCount : userCount;
    }

    public bool DungeonRound(int round) {
        DebugLog("[DungeonRound] round:{Round}", round);
        if (Field is not DungeonFieldManager dungeonField || Field.Players.IsEmpty) {
            return false;
        }
        if (!Field.TableMetadata.DungeonRoundDataTable.Entries.TryGetValue(dungeonField.DungeonMetadata.RoundId, out DungeonRoundMetadata[]? rounds)) {
            return false;
        }

        return DungeonRoundMet(rounds, round, Field.Players.Values.Min(player => player.Session.Stats.Values.GearScore));
    }

    // DungeonRoundRequire round=N passes when the room's round N (dungeonrounddata.xml entry N-1, as DungeonRoundGroup::GetRound
    // indexes it) asks for no more gear score than the players have; the colosseum rooms are single-user, the lowest score counts.
    public static bool DungeonRoundMet(IReadOnlyList<DungeonRoundMetadata> rounds, int round, int gearScore) =>
        round >= 1 && round <= rounds.Count && gearScore >= rounds[round - 1].GearScore;

    public bool CheckUser(bool negate) {
        if (negate) {
            return Field.Players.IsEmpty;
        }
        return !Field.Players.IsEmpty;
    }

    public bool UserCount(int count) {
        return Field.Players.Count == count;
    }

    public bool CountUsers(int boxId, int userTagId, int minUsers, OperatorType operatorType, bool negate) {
        DebugLog("[GetUserCount] boxId:{BoxId}, userTagId:{TagId}", boxId, userTagId);
        if (!Objects.Boxes.TryGetValue(boxId, out TriggerBox? box)) {
            return negate;
        }

        int count;
        if (userTagId > 0) {
            count = Field.Players.Values.Count(player => player.TagId == userTagId && box.Contains(player.Position));
        } else {
            count = Field.Players.Values.Count(player => box.Contains(player.Position));
        }

        minUsers = MapleSurvivalRules.ScriptCount(minUsers, Field.Arena?.Arena.ArenaType == MapleSurvivalRules.Arena);
        bool result = operatorType switch {
            OperatorType.Greater => count > minUsers,
            OperatorType.GreaterEqual => count >= minUsers,
            OperatorType.Equal => count == minUsers,
            OperatorType.LessEqual => count <= minUsers,
            OperatorType.Less => count < minUsers,
            _ => false,
        };
        return negate ? !result : result;
    }

    public bool NpcExtraData(int spawnId, string extraDataKey, int extraDataValue, OperatorType operatorType) {
        DebugLog("[GetNpcExtraData] spawnId:{SpawnId}, extraDataKey:{Key}, extraDataValue:{Value}, operatorType:{Operator}", spawnId, extraDataKey, extraDataValue, operatorType);
        var npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        if (npc is null) {
            return false;
        }
        int extraData = npc.AiExtraData.GetValueOrDefault(extraDataKey, 0);
        return operatorType switch {
            OperatorType.Greater => extraData > extraDataValue,
            OperatorType.GreaterEqual => extraData >= extraDataValue,
            OperatorType.Equal => extraData == extraDataValue,
            OperatorType.LessEqual => extraData <= extraDataValue,
            OperatorType.Less => extraData < extraDataValue,
            _ => false,
        };
    }

    // The seconds since the dungeon instance started (its room record's start tick, shared by the lobby and the
    // room fields), compared with playSeconds (GreaterEqual when the script gives no operator). Outside a dungeon,
    // or before the dungeon started, the test fails.
    public bool DungeonPlayTime(int playSeconds, OperatorType operatorType) {
        DebugLog("[GetDungeonPlayTime] playSeconds:{Seconds}, operator:{Operator}", playSeconds, operatorType);
        if (Field is not DungeonFieldManager dungeonField) {
            return false;
        }
        return ComparePlayTime(dungeonField.DungeonRoomRecord.StartTick, Field.FieldTick, playSeconds, operatorType);
    }

    public static bool ComparePlayTime(long startTick, long nowTick, int playSeconds, OperatorType operatorType) {
        if (startTick <= 0) {
            return false;
        }
        int elapsed = (int) Math.Max(0, (nowTick - startTick) / 1000);
        return CompareUserValue(elapsed, playSeconds, operatorType);
    }

    // The room record's result by name (the shipped scripts test only "Fail"); outside a dungeon room the
    // old test (an empty name) stays.
    public bool DungeonState(string checkState) {
        DebugLog("[DungeonState] checkState:{CheckState}", checkState);
        if (Field is not DungeonFieldManager dungeonField) {
            return checkState == "";
        }
        return DungeonStateMatches(dungeonField.DungeonRoomRecord.State, checkState);
    }

    public static bool DungeonStateMatches(Maple2.Model.Enum.DungeonState state, string checkState) {
        return Enum.TryParse(checkState, true, out Maple2.Model.Enum.DungeonState wanted) && Enum.IsDefined(wanted) && wanted == state;
    }

    // The first player who entered the room: the sum of that player's user mission scores, compared
    // with score. The fame challenge rooms are for one player. Outside a dungeon room the sum is 0.
    public bool DungeonFirstUserMissionScore(int score, OperatorType operatorType) {
        int sum = Field is DungeonFieldManager dungeonField ? dungeonField.DungeonRoomRecord.FirstUserMissionScore() : 0;
        DebugLog("[DungeonFirstUserMissionScore] score:{Score}, operator:{Operator}, sum:{Sum}", score, operatorType, sum);
        return CompareRate(sum, score, operatorType);
    }

    // The field's score board score compared with score, once a script in this field has called
    // score_board_set_score. Before that (no score source feeds the board) the test is the old one, the
    // threshold compared with 0, so a battle waiting on a GreaterEqual 800 does not wait for ever.
    public bool ScoreBoardScore(int score, OperatorType operatorType) {
        DebugLog("[GetScoreBoardScore] score:{Score}, operator:{Operator}", score, operatorType);
        return CompareScoreBoard(Field.ScoreBoardScore, score, operatorType);
    }

    public static bool CompareScoreBoard(int? boardScore, int score, OperatorType operatorType) {
        if (boardScore is { } value) {
            return CompareUserValue(value, score, operatorType);
        }
        return operatorType switch {
            OperatorType.Greater => score > 0,
            OperatorType.GreaterEqual => score >= 0,
            OperatorType.Equal => score == 0,
            OperatorType.LessEqual => score <= 0,
            OperatorType.Less => score < 0,
            _ => false,
        };
    }

    public bool UserValue(string key, int value, OperatorType operatorType, bool negate) {
        DebugLog("[GetUserValue] key:{Key}", key);
        bool result = CompareUserValue(Field.UserValues.Get(TriggerId, key), value, operatorType);
        return negate ? !result : result;
    }

    public static bool CompareUserValue(int userValue, int value, OperatorType operatorType) {
        return operatorType switch {
            OperatorType.Greater => userValue > value,
            OperatorType.GreaterEqual => userValue >= value,
            OperatorType.LessEqual => userValue <= value,
            OperatorType.Less => userValue < value,
            _ => userValue == value,
        };
    }

    public void DebugString(string value, string feature) {
        logger.Debug("{Value} [{Feature}]", value, feature);
    }

    public void WriteLog(string logName, string @event, int triggerId, string subEvent, int level) {
        logger.Information("{Log}: {Event}, {TriggerId}, {SubEvent}, {Level}", logName, @event, triggerId, subEvent, level);
    }

    #region Conditions
    // The current time (tests set it); null: the clock.
    internal Func<DateTimeOffset>? Clock { get; set; }

    public bool DayOfWeek(int[] dayOfWeeks, string description, bool negate) {
        int today = (int) (Clock?.Invoke() ?? DateTimeOffset.UtcNow).LocalDateTime.DayOfWeek + 1;
        if (negate) {
            return !dayOfWeeks.Contains(today);
        }
        return dayOfWeeks.Contains(today);
    }

    public bool RandomCondition(float rate, string description) {
        if (rate < 0f || rate > 100f) {
            LogOnce(logger.Error, "[RandomCondition] Invalid rate: {Rate}", rate);
            return false;
        }

        if (currentRandom >= 100f) {
            currentRandom = Random.Shared.NextSingle() * 100;
        }

        // The state's random conditions split one roll by their weights (KR rows come as 99 and 1, or four of 25):
        // a condition passes when the roll falls in its own share, below the weights before it plus its own.
        currentRandom -= rate;
        if (currentRandom >= 0f) {
            return false;
        }

        currentRandom = float.MaxValue; // Reset
        return true;
    }

    // Chosen here: one roll per pass over a state's conditions, so a set of weights that does not reach 100 keeps
    // its odds on every tick instead of using up the roll.
    internal void NewConditionPass() {
        currentRandom = float.MaxValue;
    }

    public bool WaitAndResetTick(int waitTick) {
        long tickNow = Environment.TickCount64;
        if (tickNow <= StartTick + waitTick) {
            return false;
        }

        StartTick = tickNow;
        return true;
    }

    public bool WaitTick(int waitTick) {
        return Environment.TickCount64 > StartTick + waitTick;
    }
    #endregion
}

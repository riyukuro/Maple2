using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BasicAttribute = Maple2.Model.Enum.BasicAttribute;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Scheduler;
using Serilog;
using Serilog.Core;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext : ITriggerContext {
    private readonly FieldTrigger owner;
    private readonly ILogger logger = Log.Logger.ForContext<TriggerContext>();

    private FieldManager Field => owner.Field;
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
    public bool ShadowExpeditionPoints(int score) {
        ErrorLog("[GetShadowExpeditionPoints]");
        return 0 >= score;
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

    public static bool CompareRate(double rate, float value, OperatorType operatorType) {
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

    public bool DungeonId(int dungeonId) {
        ErrorLog("[GetDungeonId]");
        return dungeonId == 0;
    }

    public bool DungeonLevel(int level) {
        ErrorLog("[GetDungeonLevel]");
        return level == 3;
    }

    public bool DungeonMaxUserCount(int value) {
        ErrorLog("[GetDungeonMaxUserCount]");
        return value == 1;
    }

    public bool DungeonRound(int round) {
        ErrorLog("[GetDungeonRoundsRequired]");
        return int.MaxValue == round;
    }

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

    // Scripts seem to just check if this is "Fail"
    public bool DungeonState(string checkState) {
        ErrorLog("[GetDungeonState]");
        return checkState == "";
    }

    public bool DungeonFirstUserMissionScore(int score, OperatorType operatorType) {
        ErrorLog("[GetDungeonFirstUserMissionScore]");
        return operatorType switch {
            OperatorType.Greater => score > 0,
            OperatorType.GreaterEqual => score >= 0,
            OperatorType.Equal => score == 0,
            OperatorType.LessEqual => score <= 0,
            OperatorType.Less => score < 0,
            _ => false,
        };
    }

    // The field's score board score (set by score_board_set_score, reset by score_board_create and
    // score_board_remove) compared with score.
    public bool ScoreBoardScore(int score, OperatorType operatorType) {
        DebugLog("[GetScoreBoardScore] score:{Score}, operator:{Operator}", score, operatorType);
        return CompareUserValue(Field.ScoreBoardScore, score, operatorType);
    }

    public bool UserValue(string key, int value, OperatorType operatorType, bool negate) {
        DebugLog("[GetUserValue] key:{Key}", key);
        bool result = CompareUserValue(Field.UserValues.GetValueOrDefault(key, 0), value, operatorType);
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
    public bool DayOfWeek(int[] dayOfWeeks, string description, bool negate) {
        if (negate) {
            return !dayOfWeeks.Contains((int) DateTime.UtcNow.DayOfWeek + 1);
        }
        return dayOfWeeks.Contains((int) DateTime.UtcNow.DayOfWeek + 1);
    }

    public bool RandomCondition(float rate, string description) {
        if (rate < 0f || rate > 100f) {
            LogOnce(logger.Error, "[RandomCondition] Invalid rate: {Rate}", rate);
            return false;
        }

        if (currentRandom >= 100f) {
            currentRandom = Random.Shared.NextSingle() * 100;
        }

        currentRandom -= rate;
        if (currentRandom > rate) {
            return false;
        }

        currentRandom = float.MaxValue; // Reset
        return true;
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

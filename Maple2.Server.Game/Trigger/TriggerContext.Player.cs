using System.Numerics;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void EnableSpawnPointPc(int spawnPointId, bool enabled) {
        DebugLog("[EnableSpawnPointPc] spawnPointId:{Type}, enabled:{Enabled}", spawnPointId, enabled);
        if (!Field.TryGetPlayerSpawn(spawnPointId, out FieldPlayerSpawnPoint? fieldPlayerSpawn)) {
            logger.Error("[EnableSpawnPointPc] Invalid Player Spawn Point ID: {spawnPointId} in Map ID: {mapId}", spawnPointId, Field.MapId);
            return;
        }

        fieldPlayerSpawn.Enable = enabled;
    }

    public void GiveExp(int boxId, float expRate, bool arg3) {
        ErrorLog("[GiveExp] boxId:{BoxId}, expRate:{ExpRate}", boxId, expRate);
    }

    // Every player in the field gets the reward content (rewardcontent.xml): its exp, meso and item tables, the
    // same reward the mini games give. The field war maps give it to the whole field after each phase.
    public void GiveRewardContent(int rewardId) {
        DebugLog("[GiveRewardContent] rewardId:{RewardId}", rewardId);
        foreach (FieldPlayer player in Field.Players.Values) {
            player.Session.GetRewardContent(rewardId);
        }
    }

    public void KickMusicAudience(int targetBoxId, int targetPortalId) {
        DebugLog("[KickMusicAudience] targetBoxId:{BoxId}, targetPortalId:{PortalId}", targetBoxId, targetPortalId);
        if (Field.PerformanceStage is null) {
            return;
        }
        if (!Field.TryGetPortal(targetPortalId, out FieldPortal? portal)) {
            return;
        }

        foreach (FieldPlayer player in PlayersInBox(targetBoxId)) {
            // Don't kick the current performer
            if (Field.PerformanceStage.IsCurrentPerformer(player.Session)) {
                continue;
            }
            player.MoveToPortal(portal);
        }
    }

    public void MoveRandomUser(int mapId, int portalId, int boxId, int count) {
        DebugLog("[MoveRandomUser] mapId:{MapId}, portalId:{PortalId}, boxId:{BoxId}, count:{Count}", mapId, portalId, boxId, count);
        FieldPlayer[] players = PlayersInBox(boxId).ToArray();
        Random.Shared.Shuffle(players);
        if (count > players.Length) {
            count = players.Length;
        }

        for (int i = 0; i < count; i++) {
            FieldPlayer player = players[i];
            if (mapId == 0) {
                if (portalId == 0) {
                    player.Session.ReturnField();
                    return;
                }

                if (!Field.TryGetPortal(portalId, out FieldPortal? portal)) {
                    return;
                }
                player.MoveToPortal(portal);
                return;
            }

            player.Session.Send(player.Session.PrepareField(mapId, portalId)
                ? FieldEnterPacket.Request(player)
                : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        }
    }

    public void MoveToPortal(int userTagId, int portalId, int boxId) {
        DebugLog("[MoveToPortal] userTagId:{TagId}, portalId:{PortalId}, boxId:{BoxId}", userTagId, portalId, boxId);
        if (!Field.TryGetPortal(portalId, out FieldPortal? portal)) {
            return;
        }

        foreach (FieldPlayer player in PlayersInBox(boxId)) {
            if (userTagId <= 0 || player.TagId == userTagId) {
                player.MoveToPortal(portal);
            }
        }
    }

    public void MoveUser(int mapId, int portalId, int boxId) {
        DebugLog("[MoveUser] mapId:{MapId}, portalId:{PortalId}, boxId:{BoxId}", mapId, portalId, boxId);
        if (mapId == 0) {
            foreach (FieldPlayer player in PlayersInBox(boxId)) {
                player.Session.ReturnField();
            }
            return;
        }

        if (mapId == Field.MapId) {
            if (!Field.TryGetPortal(portalId, out FieldPortal? portal)) {
                return;
            }

            foreach (FieldPlayer player in PlayersInBox(boxId)) {
                player.MoveToPortal(portal);
            }
            return;
        }

        foreach (FieldPlayer player in PlayersInBox(boxId)) {
            player.Session.Send(player.Session.PrepareField(mapId, portalId)
                ? FieldEnterPacket.Request(player)
                : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        }
    }

    public void MoveUserPath(string path) {
        DebugLog("[MoveUserPath] path:{Path}", path);

        Field.MovePlayerAlongPath(path);
    }

    public void MoveUserToBox(int boxId, int portalId) {
        DebugLog("[MoveUserToBox] boxId:{BoxId}, portalId:{PortalId}", boxId, portalId);
        if (!Field.TryGetPortal(portalId, out FieldPortal? portal)) {
            return;
        }

        foreach (FieldPlayer player in PlayersNotInBox(boxId)) {
            player.MoveToPortal(portal);
        }
    }

    public void MoveUserToPos(Vector3 position, Vector3 rotation) {
        DebugLog("[MoveUserToPos] position:{Position}, rotation:{Rotation}", position, rotation);
        foreach (FieldPlayer player in Field.Players.Values) {
            player.MoveToPosition(position, rotation);
        }
    }

    public void PatrolConditionUser(string patrolName, int patrolIndex, int additionalEffectId) {
        ErrorLog("[PatrolConditionUser] patrolName:{Name}, patrolIndex:{Index}, additionalEffectId:{EffectId}", patrolName, patrolIndex, additionalEffectId);
    }

    public void SetAchievement(int triggerId, string type, string code) {
        DebugLog("[SetAchievement] type:{Type}, code:{Code}, triggerId:{TriggerId}", type, code, triggerId);

        type = string.IsNullOrWhiteSpace(type) ? "trigger" : type;
        if (!Enum.TryParse<ConditionType>(type, out ConditionType conditionType)) {
            conditionType = ConditionType.unknown;
        }

        foreach (FieldPlayer player in PlayersInBox(triggerId)) {
            player.Session.ConditionUpdate(conditionType, codeString: code);
        }
    }

    public void SetPcEmotionLoop(string sequenceName, float duration, bool loop) {
        DebugLog("[SetPcEmotionLoop] sequenceName:{SequenceName}, duration:{Duration}, arg3:{Arg3}", sequenceName, duration, loop);
        Broadcast(TriggerPacket.UiEmotionLoop(sequenceName, (int) duration, loop));
    }

    public void SetPcEmotionSequence(string[] sequenceNames) {
        DebugLog("[SetPcEmotionSequence] sequenceNames:{SequenceNames}", string.Join(", ", sequenceNames));
        Broadcast(TriggerPacket.UiEmotionSequence(sequenceNames));
    }

    public void SetPcRotation(Vector3 rotation) {
        DebugLog("[SetPcRotation] rotation:{Rotation}", rotation);
        Broadcast(TriggerPacket.UiPlayerRotation(rotation));
    }

    // 52000200's comment calls it a forced quest accept, so the quest's requirements are skipped.
    public void SetQuestAccept(int questId) {
        DebugLog("[SetQuestAccept] questId:{QuestId}", questId);
        foreach (FieldPlayer player in Field.Players.Values) {
            player.Session.Quest.Start(questId, bypassRequirements: true); // a quest the player already has is left alone
        }
    }

    // Completes the quest for each player who has it started; the script decides when it is done, so its conditions are skipped.
    public void SetQuestComplete(int questId) {
        DebugLog("[SetQuestComplete] questId:{QuestId}", questId);
        foreach (FieldPlayer player in Field.Players.Values) {
            if (player.Session.Quest.TryGetQuest(questId, out Quest? quest) && CanForceCompleteQuest(quest.State)) {
                player.Session.Quest.Complete(quest, bypassConditions: true);
            }
        }
    }

    public static bool CanForceCompleteQuest(QuestState state) => state == QuestState.Started;

    // The npc of the spawn point starts its talk with each player, as when clicked (the Colosseum's Nagi offers
    // a new game or the continue; the player's choice sets DungeonPlayType through the npc's script function).
    public void TalkNpc(int spawnId) {
        DebugLog("[TalkNpc] spawnId:{SpawnId}", spawnId);
        FieldNpc? npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        if (npc == null) {
            return;
        }
        foreach (FieldPlayer player in Field.Players.Values) {
            NpcTalkHandler.BeginTalk(player.Session, npc);
        }
    }

    public void AddUserValue(string key, int value) {
        WarnLog("[AddUserValue] key:{Key}, value:{Value}", key, value);
        if (Field.UserValues.Contains(TriggerId, key)) {
            Field.UserValues.Add(TriggerId, key, value);
        }
    }

    public void SetUserValue(int triggerId, string key, int value) {
        WarnLog("[SetUserValue] key:{Key}, value:{Value}, triggerId:{TriggerId}", key, value, triggerId);
        Field.UserValues.Set(TriggerUserValues.Target(TriggerId, triggerId), key, value);
    }

    public void FaceEmotion(int spawnId, string emotionName) {
        DebugLog("[FaceEmotion] spawnId:{SpawnId}, emotionName:{Emotion}", spawnId, emotionName);
        if (spawnId == 0) {
            FieldPlayer? firstPlayer = Field.Players.Values.FirstOrDefault();
            if (firstPlayer == null) {
                return;
            }
            Field.Broadcast(TriggerPacket.UiFaceEmotion(firstPlayer.ObjectId, emotionName));
            return;
        }

        foreach (var npc in Field.Npcs.Values.Where(npc => npc.SpawnPointId == spawnId)) {
            Field.Broadcast(TriggerPacket.UiFaceEmotion(npc.ObjectId, emotionName));
        }
    }

    public void SetState(int triggerId, string[] states, bool randomize) {
        ErrorLog("[SetState] triggerId:{TriggerId}, states:{States}, randomize:{Randomize}", triggerId, string.Join(", ", states), randomize);
        if (randomize) {
            Random.Shared.Shuffle(states);
        }

        Field.States[triggerId] = owner.GetStates(states);
    }

    #region Conditions
    public bool CheckAnyUserAdditionalEffect(int boxId, int additionalEffectId, int level, bool negate) {
        DebugLog("[CheckAnyUserAdditionalEffect] boxId:{BoxId}, additionalEffectId:{EffectId}, level:{Level}", boxId, additionalEffectId, level);
        foreach (FieldPlayer player in PlayersInBox(boxId)) {
            if (player.Buffs.HasBuff(additionalEffectId, (short) level)) {
                return !negate;
            }
        }

        return negate;
    }

    // True when the box holds at least one player and all of them carry the same user tag.
    public bool CheckSameUserTag(int boxId, bool negate) {
        DebugLog("[CheckSameUserTag] boxId:{BoxId}, negate:{Negate}", boxId, negate);
        bool result = SameUserTag(PlayersInBox(boxId).Select(player => player.TagId));
        return negate ? !result : result;
    }

    public static bool SameUserTag(IEnumerable<int> tagIds) {
        int? first = null;
        foreach (int tagId in tagIds) {
            if (first == null) {
                first = tagId;
            } else if (first != tagId) {
                return false;
            }
        }
        return first != null;
    }

    public bool QuestUserDetected(int[] boxIds, int[] questIds, int[] questStates, int jobCode, bool negate) {
        DebugLog("[QuestUserDetected] boxIds:{BoxIds}, questIds:{QuestIds}, questStates:{QuestStates}, jobCode:{JobCode}",
            string.Join(", ", boxIds), string.Join(", ", questIds), string.Join(", ", questStates), (JobCode) jobCode);

        foreach (FieldPlayer player in PlayersInBox(boxIds)) {
            if (!TriggerJobCode.Matches(player.Value.Character.Job.Code(), jobCode)) {
                continue;
            }

            foreach (int questId in questIds) {
                if (!player.Session.Quest.TryGetQuest(questId, out Quest? quest)) {
                    continue;
                }

                foreach (int questState in questStates) {
                    switch (questState) {
                        case 1: // Started (but NOT ready to complete)
                            if (quest.State == QuestState.Started && !player.Session.Quest.CanComplete(quest)) {
                                return !negate;
                            }
                            break;
                        case 2: // Started and Can Complete
                            if (quest.State == QuestState.Started && player.Session.Quest.CanComplete(quest)) {
                                return !negate;
                            }
                            break;
                        case 3: // Completed
                            if (quest.State == QuestState.Completed) {
                                return !negate;
                            }
                            break;
                    }
                }
            }
        }

        return negate;
    }

    public bool UserDetected(int[] boxIds, JobCode jobCode, bool negate) {
        DebugLog("[UserDetected] boxIds:{BoxIds}, jobCode:{JobCode}", string.Join(", ", boxIds), (JobCode) jobCode);
        IEnumerable<TriggerBox> boxes = boxIds
            .Select(boxId => Objects.Boxes.GetValueOrDefault(boxId))
            .Where(box => box != null)!;

        bool result;
        if (jobCode != 0) {
            result = Field.Players.Values
                .Any(player => player.Value.Character.Job.Code() == jobCode && boxes.Any(box => box.Contains(player.Position)));
        } else {
            result = Field.Players.Values.Any(player => boxes.Any(box => box.Contains(player.Position)));
        }

        return negate ? !result : result;
    }

    // Waits as many seconds, since the state was entered, as the user value key holds (the shipped desc: the
    // time event gives the hold time; when it ends the script goes back). An unset key reads 0, as user_value does.
    public bool WaitSecondsUserValue(string key, string desc) {
        DebugLog("[WaitSecondsUserValue] key:{Key}", key);
        return WaitSecondsElapsed(StartTick, Environment.TickCount64, Field.UserValues.Get(TriggerId, key));
    }

    public static bool WaitSecondsElapsed(long startTick, long nowTick, int seconds) {
        return nowTick > startTick + seconds * 1000L;
    }
    #endregion

    private IEnumerable<FieldPlayer> PlayersInBox(params int[] boxIds) {
        if (boxIds.Length == 0 || boxIds[0] == 0) {
            return Field.Players.Values;
        }

        IEnumerable<TriggerBox> boxes = boxIds
            .Select(boxId => Objects.Boxes.GetValueOrDefault(boxId))
            .Where(box => box != null)!;

        return Field.Players.Values.Where(player => boxes.Any(box => box.Contains(player.Position)));
    }

    private IEnumerable<FieldPlayer> PlayersNotInBox(int boxId) {
        if (!Objects.Boxes.TryGetValue(boxId, out TriggerBox? box)) {
            return [];
        }

        return Field.Players.Values.Where(player => !box.Contains(player.Position));
    }
}

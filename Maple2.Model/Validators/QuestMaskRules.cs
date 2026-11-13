using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The client's quest mask of an npc in a field (table/questmasknpc.xml), which it applies to every npc it is sent:
// CProxyGameObject::SetTemplateId and UpdateQuestState (client/ms2/proxygameobject.cpp:369, 391) hide a masked npc
// whose mask fails, and the quest marks and the world map skip it.
public static class QuestMaskRules {
    // FUN_140764480 (commonfiles/questmasktable.cpp:1196): a quest against a state mode, 0 met, 1 passed, 2 not
    // reached. The client's "completed" is a started quest whose conditions are all met, "rewarded" is state 2.
    public static int QuestStateOf(QuestState? state, bool conditionsMet, int mode) {
        bool absent = state == null;
        bool active = state == QuestState.Started && !conditionsMet;
        bool completed = state == QuestState.Started && conditionsMet;
        bool rewarded = state == QuestState.Completed;
        switch (mode) {
            case 1:
                if (absent) return 2;
                if (active) return 0;
                if (completed || rewarded) return 1;
                break;
            case 2:
                if (absent || active) return 2;
                if (completed) return 0;
                if (rewarded) return 1;
                break;
            case 3:
                if (rewarded) return 0;
                break;
        }
        return 2;
    }

    // CQuestManagerCommon::vfunc_o10_23 (commonfiles/questdata.cpp:722): a mask without a valid range shows the npc;
    // else the first range whose start is reached and whose end is not passed gives Enable, and none gives !Enable.
    // quest: the player's state of a quest id (null when the player has none) and whether its conditions are met.
    public static bool Visible(QuestMaskNpcTable.Mask mask, Func<int, (QuestState? State, bool ConditionsMet)> quest) {
        if (!Valid(mask)) {
            return true;
        }
        foreach (QuestMaskNpcTable.Range range in mask.Ranges) {
            (QuestState? startState, bool startMet) = quest(range.StartQuestId);
            (QuestState? endState, bool endMet) = quest(range.EndQuestId);
            if (QuestStateOf(startState, startMet, range.StartState) == 2) continue;
            if (QuestStateOf(endState, endMet, range.EndState) == 1) continue;
            return mask.Enable;
        }
        return !mask.Enable;
    }

    // QuestMask::IsSatisfied: at least one range, each with a start quest. LoadFile keeps only such masks.
    public static bool Valid(QuestMaskNpcTable.Mask mask) {
        return mask.Ranges.Count > 0 && mask.Ranges.All(range => range.StartQuestId != 0);
    }

    // The client shows a masked npc only if the server spawned it, and no map places some enableInMask=1 npcs: Karl
    // 11004779 of 02000065, whose quests 10003408 to 10003418 open the lv75 epic, is in no xblock, trigger or table.
    // Chosen here: such an npc stands at the spawn point of an enableInMask=0 npc of the same field whose ranges are
    // the same (the npc it stands in for: the client shows one of the two by the player's quests). The partner must
    // be at a regular spawn point of the field; the first partner by npc id and its first point is used. The new point
    // keeps the partner's position, rotation and spawn flags with the one npc, and no spawn id: a trigger that names the
    // partner's spawn id finds only the partner. An npc a regular or an event
    // spawn point of the field already names is left alone.
    public static IEnumerable<SpawnPointNPC> PartnerSpawns(IReadOnlyDictionary<int, QuestMaskNpcTable.Mask>? masks,
                                                           IReadOnlyList<SpawnPointNPC> npcSpawns,
                                                           IEnumerable<SpawnPointNPC> eventNpcSpawns) {
        if (masks == null || masks.Count == 0) {
            yield break;
        }
        var placed = npcSpawns.Concat(eventNpcSpawns).SelectMany(spawn => spawn.NpcList).Select(entry => entry.NpcId).ToHashSet();
        foreach ((int npcId, QuestMaskNpcTable.Mask mask) in masks.OrderBy(entry => entry.Key)) {
            if (!mask.Enable || !Valid(mask) || placed.Contains(npcId)) {
                continue;
            }
            foreach ((int partnerId, QuestMaskNpcTable.Mask partner) in masks.OrderBy(entry => entry.Key)) {
                if (partner.Enable || !partner.Ranges.SequenceEqual(mask.Ranges)) {
                    continue;
                }
                SpawnPointNPC? point = npcSpawns.FirstOrDefault(spawn => spawn.NpcList.Any(entry => entry.NpcId == partnerId));
                if (point == null) {
                    continue;
                }
                yield return point with {
                    EntityId = $"{point.EntityId}_mask{npcId}",
                    SpawnPointId = 0,
                    NpcList = [new SpawnPointNPCListEntry(npcId, 1)],
                    PatrolData = null,
                };
                break;
            }
        }
    }
}

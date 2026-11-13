using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The quests no client UI accepts, which the server grants (FORK.md "Daily quest grants"): the daily field quest
// (questType 8), the week's mission point ladder (questType 3, guide mission_point) and the day's daily missions
// (questType 3, guide daily_pick_once/daily_pick_repeat). The client's quest-accept check leaves them out (FUN_140746e60, questdata.cpp:673) and its pages only
// claim them, so a player holds one only when the server starts it. The grant runs at login and at the daily reset;
// each kind is granted once per period, kept as the start of the last period granted (Character), so a relog, a
// second reset or an abandon in the same period grants nothing again.
public static class DailyQuestGrant {
    // The group of the daily field quests (require groupID of all 90 KR field quests). It has no questgrouptable row,
    // so the client accepts one pick a day, the same for every player (QuestGroupPick).
    public const int FieldQuestGroup = 91000000;

    // A daily field quest: questType 8, repeatable 2, in the field quest group.
    public static bool IsFieldQuest(QuestMetadata metadata) {
        return metadata.Basic.Type == QuestType.FieldQuest && metadata.Basic.Repeat == 2 && metadata.Require.GroupId == FieldQuestGroup;
    }

    // A mission point mission: questType 3 of guide mission_point. The level bar (UIDailyMissionDialog::RefreshLevelGuide,
    // uidailymissiondialog.cpp:369) lists every one of the table and shows a row only for a held one.
    public static bool IsMissionPoint(QuestMetadata metadata) {
        return metadata.Basic.Type == QuestType.DailyMission && metadata.Basic.GuideType == QuestDailyReset.GuideMissionPoint;
    }

    // A daily mission: questType 3 of guide daily_pick_once or daily_pick_repeat. The daily mission dialog lists the
    // held ones of the selected guideGroup tab, and lights a tab only while one of its group is held
    // (uidailymissiondialog.cpp:337, 657).
    public static bool IsDailyMission(QuestMetadata metadata) {
        return metadata.Basic.Type == QuestType.DailyMission
               && metadata.Basic.GuideType is QuestDailyReset.GuideDailyPickOnce or QuestDailyReset.GuideDailyPickRepeat;
    }

    // The dialog's tabs: guideGroup 0 levelup, 1 community, 2 special, 3 specup, 4 farmmeso, 5 farmingredient
    // (commonenum.cpp:800).
    public const int DailyMissionGroups = 6;

    // Chosen here: the daily missions granted a day in each group whose band holds the level. No client table gives
    // a count (FORK.md "Daily quest grants").
    public const int DailyMissionsPerGroup = 1;

    // The candidates of a group in the order the character tries them on the day: id order shuffled by the seed of
    // the character, the day and the group (AllianceMissions.Seed), so a relog or a second reset picks the same.
    public static List<QuestMetadata> DailyMissionOrder(IEnumerable<QuestMetadata> candidates, long characterId, DateTime day, int group) {
        List<QuestMetadata> list = candidates.OrderBy(metadata => metadata.Id).ToList();
        var random = new Random(AllianceMissions.Seed(characterId, day, group));
        for (int i = list.Count - 1; i > 0; i--) {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    // Whether the quest's guide band holds the level: guideMinLevel to guideMaxLevel, both included (0: no upper end).
    public static bool InBand(QuestMetadataBasic basic, int level) {
        return level >= basic.GuideMinLevel && (basic.GuideMaxLevel == 0 || level <= basic.GuideMaxLevel);
    }

    // The start of the mission point week now is in: the last Friday start at or before now, the day the client's
    // 0x19 drops the missions of the week before (QuestDailyReset.DailyMissionDropped).
    public static DateTime MissionPointWeekStart(DateTime now) {
        return QuestRules.NextWeekdayStart(QuestDailyReset.MissionPointWeekday, now).AddDays(-7);
    }

    // Whether the period that starts at periodStart (local) is still to be granted: the last grant (epoch seconds,
    // 0 never) came before it.
    public static bool Due(long lastGrant, DateTime periodStart) {
        return lastGrant < periodStart.ToEpochSeconds();
    }
}

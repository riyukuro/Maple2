using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The three daily quest resets the client takes from the server (CQuestManager::OnQuest, questmanager_2.cpp): Quest
// 0x19 drops daily missions, 0x1E daily field quests, 0x26 wedding missions. The client drops them in every state,
// started or completed, so the server drops the same rows: at the reset all of them (Dropped with atReset), and at
// login those a missed reset would have dropped (Dropped without atReset: started before today, or for mission
// points past their Friday).
public static class QuestDailyReset {
    public enum Kind {
        None,
        DailyMission,
        FieldQuest,
        WeddingMission,
    }

    // guide guideType (QuestMetadataBasic.GuideType).
    public const int GuideDailyPickOnce = 3;
    public const int GuideDailyPickRepeat = 4;
    public const int GuideMissionPoint = 5;
    // The weekday the mission point missions end on: NextWeekdayStart(6, start) in the client's 0x19 (6 is Friday).
    public const int MissionPointWeekday = 6;

    // The reset that drops a quest of this kind, or None for a quest no reset drops. Daily missions (questType 3)
    // are dropped by guide (the level and level_boost ones never); field quests (questType 8) only when daily
    // (repeatable 2); wedding missions (questType 10) all.
    public static Kind KindOf(QuestMetadataBasic basic) {
        return basic.Type switch {
            QuestType.DailyMission when basic.GuideType is GuideDailyPickOnce or GuideDailyPickRepeat or GuideMissionPoint => Kind.DailyMission,
            QuestType.FieldQuest when basic.Repeat == 2 => Kind.FieldQuest,
            QuestType.WeddingMission => Kind.WeddingMission,
            _ => Kind.None,
        };
    }

    // The client's 0x19 per daily mission: guide 3 and 4 always; guide 5 when now is at or past the start of the
    // Friday after its start (NextWeekdayStart, timeutil.cpp:403). Local times, as the client's CMsTime.
    public static bool DailyMissionDropped(int guideType, DateTime start, DateTime now) {
        return guideType switch {
            GuideDailyPickOnce or GuideDailyPickRepeat => true,
            GuideMissionPoint => now >= QuestRules.NextWeekdayStart(MissionPointWeekday, start),
            _ => false,
        };
    }

    // Whether the reset (atReset) or the login check drops a held quest started at startTime (epoch seconds). At the
    // reset the server drops what the client drops on the packets. At login (no packet: the client gets only the rows
    // the server sends) a daily row goes when it was started before today, a mission point row when its Friday has
    // come.
    public static bool Dropped(QuestMetadataBasic basic, long startTime, DateTime now, bool atReset) {
        Kind kind = KindOf(basic);
        if (kind == Kind.None) {
            return false;
        }
        DateTime start = DateTimeOffset.FromUnixTimeSeconds(startTime).LocalDateTime;
        if (kind == Kind.DailyMission && basic.GuideType == GuideMissionPoint) {
            return DailyMissionDropped(basic.GuideType, start, now);
        }
        return atReset || start.Date < now.Date;
    }
}

using Maple2.Model.Game.Dungeon;

namespace Maple2.Model.Validators;

// RoomDungeon 0x002B mode 2 (create): i32 dungeon id, bool with party, i32 portal id, bool use ticket.
// The client's room window enables the solo button only for a dungeon without a minimum user
// count (or in its debug state) and the party button only for a party in a dungeon that holds
// more than one user.
public static class DungeonRoomRules {
    public enum CreateVerdict {
        Ok,
        UnderMinUserCount,
        OverMaxUserCount,
    }

    public static CreateVerdict CanCreate(bool withParty, int partySize, int minUserCount, int maxUserCount, bool gm) {
        if (!withParty) {
            return minUserCount == 0 || gm ? CreateVerdict.Ok : CreateVerdict.UnderMinUserCount;
        }
        if (maxUserCount <= 1 || partySize > maxUserCount) {
            return CreateVerdict.OverMaxUserCount;
        }
        return CreateVerdict.Ok;
    }

    // The client's DungeonRecordCli::HasCountLeft (dungeonrecordcli.cpp:148) on the record as the server writes
    // it (DungeonRecord.WriteTo): the expire is UnionCooldownTimestamp, the rewards taken are UnionClears, the
    // timed clear count is UnionSubClears until UnionSubCooldownTimestamp, and ExtraSubClears is the bonus count
    // added to rewardCount. Each part counts only while now is before its expire. A missing record has a count.
    public static bool HasCountLeft(DungeonRecord? record, int rewardCount, int subRewardCount, long now) {
        if (record == null) {
            return true;
        }
        bool expired = now >= record.UnionCooldownTimestamp;
        if (subRewardCount > 0) {
            int clears = now >= record.UnionSubCooldownTimestamp ? 0 : record.UnionSubClears;
            if (clears >= subRewardCount) {
                return false;
            }
        }
        int taken = expired ? 0 : record.UnionClears;
        int max = rewardCount + (expired ? 0 : record.ExtraSubClears);
        return taken < max;
    }

    // limitDayOfWeeks: the days a room can be entered, none for every day. The client reads the list as day
    // numbers with Sunday 1 (DungeonRoom ReadDayBits, commonfiles/dungeonroom.cpp:408) and tests the bit of
    // the server time's day, CMsTime::GetDayOfWeek = tm_wday + 1 (wireframemanager.cpp:126); the ingest maps
    // those numbers to the same days. Its refusal is DungeonRecordManagerCli::OnError 0xc,
    // s_room_dungeon_canEnterDayOfWeeks (dungeonrecordcli.cpp:371).
    public static bool OpenOnDay(IReadOnlyCollection<DayOfWeek> days, DayOfWeek today) =>
        days.Count == 0 || days.Contains(today);
}

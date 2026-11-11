using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;

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
    // An event count (eventCount, the DungeonExtraReward event's count of the room, 0 without one) not used up
    // gives a count first, before the daily limit (DungeonRecordCli::IsExtraExhausted, dungeonrecordcli.cpp:160).
    public static bool HasCountLeft(DungeonRecord? record, int rewardCount, int subRewardCount, long now, int eventCount = 0) {
        if (record == null) {
            return true;
        }
        if (ExtraCountNow(record, now) < eventCount) {
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

    // The event reward counts used (DungeonRecordCli::GetExtraCountNow, dungeonrecordcli.cpp:110): ExtraClears until
    // ExtraCooldownTimestamp, 0 after it or without a record.
    public static int ExtraCountNow(DungeonRecord? record, long now) =>
        record == null || now >= record.ExtraCooldownTimestamp ? 0 : record.ExtraClears;

    // The room's count of the running DungeonExtraReward events: the first event by id that names it, as the
    // client keeps the first (CGameRule::AddDungeonExtraRewardCount logs a later one as a duplicate).
    public static (int EventId, DungeonExtraReward.Entry Entry)? ExtraRewardEntry(
            IEnumerable<(int EventId, DungeonExtraReward Data)> events, int dungeonId) {
        foreach ((int eventId, DungeonExtraReward data) in events.OrderBy(gameEvent => gameEvent.EventId)) {
            foreach (DungeonExtraReward.Entry entry in data.Entries) {
                if (entry.DungeonId == dungeonId) {
                    return (eventId, entry);
                }
            }
        }
        return null;
    }

    // A clear past the room's reward count takes an event count while one is left. The event's cooldown is not sent to
    // the client; its counts end at the cooldown's end as a room's do (CooldownEnd), or with the event without one.
    public static bool UseExtraCount(DungeonRecord record, DungeonExtraReward.Entry entry, DateTime clearTime, DateTime eventEnd) {
        long now = new DateTimeOffset(clearTime).ToUnixTimeSeconds();
        if (ExtraCountNow(record, now) >= entry.Count) {
            return false;
        }
        if (now >= record.ExtraCooldownTimestamp) {
            record.ExtraClears = 0;
            DateTime end = entry.CooldownType == DungeonCooldownType.none
                ? eventEnd : CooldownEnd(entry.CooldownType, entry.CooldownValue, clearTime);
            record.ExtraCooldownTimestamp = new DateTimeOffset(end).ToUnixTimeSeconds();
        }
        record.ExtraClears++;
        return true;
    }

    // The extra reward counts bought in the record's period (DungeonRecordCli::GetBonusCountNow,
    // dungeonrecordcli.cpp:122): ExtraSubClears until UnionCooldownTimestamp, 0 after it or without a record.
    public static int BonusCountNow(DungeonRecord? record, long now) =>
        record == null || now >= record.UnionCooldownTimestamp ? 0 : record.ExtraSubClears;

    // The tickets one more extra reward count costs (DungeonRoom::GetExtraRewardTicketCost, dungeonroom.cpp:618): the
    // room's base count times the coupon rate at the bought count, the last rate once the count is past the list.
    public static int ExtraTicketCost(DungeonExtraRewardTicket ticket, int bonusCount) {
        if (ticket.BaseRates.Length == 0) {
            return 0;
        }
        return ticket.BaseCount * ticket.BaseRates[Math.Clamp(bonusCount, 0, ticket.BaseRates.Length - 1)];
    }

    public enum ExtraRewardVerdict {
        Ok,
        NoTicket,
        AtMax,
    }

    // Whether one more extra reward count can be bought. NoTicket (the client's OnError 5,
    // s_room_dungeon_reward_CantUseExtraReward): the room has no coupon, it costs nothing, or its ticket tag is not
    // one the server knows. AtMax (OnError 7, s_room_dungeon_max_reward_count): the coupon's maxExtraCount is
    // reached, or the record's byte count is full; a coupon without maxExtraCount has no other limit (the room window
    // then shows no maximum, uidungeonroomdialog.cpp:923).
    public static ExtraRewardVerdict CanAddExtraReward(DungeonExtraRewardTicket? ticket, int bonusCount) {
        if (ticket == null || ticket.TicketTag == ItemTag.None || ExtraTicketCost(ticket, bonusCount) <= 0) {
            return ExtraRewardVerdict.NoTicket;
        }
        if ((ticket.MaxExtraCount > 0 && bonusCount >= ticket.MaxExtraCount) || bonusCount >= byte.MaxValue) {
            return ExtraRewardVerdict.AtMax;
        }
        return ExtraRewardVerdict.Ok;
    }

    // Whether a veteran can claim a room's helper reward (DungeonRecordManagerCli::CanClaimHelperReward,
    // dungeonrecordcli.cpp:509): the room sets dungeonHelperRequireClearCount, and the record is a veteran's
    // (DungeonRecordFlag.Veteran) with at least that many clears.
    public static bool CanClaimHelperReward(DungeonRecord? record, int requireCount) =>
        requireCount > 0 && record != null && record.Flag.HasFlag(DungeonRecordFlag.Veteran) && record.TotalClears >= requireCount;

    // Whether a player other than characterId cleared the room as a rookie: veterans holds, for each player of the
    // clear, whether their record of the room is a veteran's. The expert notice (s_room_dungeon_record_notify_change_expert)
    // says a veteran collects helper rewards by clearing with rookies.
    public static bool WithOtherRookie(long characterId, IReadOnlyDictionary<long, bool> veterans) =>
        veterans.Any(entry => entry.Key != characterId && !entry.Value);

    // A party member's enter state for a room blocks the party's entry at 10 and above, as the client tests it
    // (CPartyManager::CanEnterDungeonRoom, partymanager.cpp:561; a missing room counts as 0). The room window
    // shows each member's state but its party button sends the request anyway (UIDungeonRoomInfo::OnEnterParty).
    // The first blocking limit in member order, null when none blocks.
    public static DungeonEnterLimit? PartyBlockingLimit(IEnumerable<IReadOnlyDictionary<int, DungeonEnterLimit>> members, int dungeonId) {
        foreach (IReadOnlyDictionary<int, DungeonEnterLimit> limits in members) {
            if (limits.TryGetValue(dungeonId, out DungeonEnterLimit limit) && (byte) limit >= 10) {
                return limit;
            }
        }
        return null;
    }

    // limitDayOfWeeks: the days a room can be entered, none for every day. The client reads the list as day
    // numbers with Sunday 1 (DungeonRoom ReadDayBits, commonfiles/dungeonroom.cpp:408) and tests the bit of
    // the server time's day, CMsTime::GetDayOfWeek = tm_wday + 1 (wireframemanager.cpp:126); the ingest maps
    // those numbers to the same days. Its refusal is DungeonRecordManagerCli::OnError 0xc,
    // s_room_dungeon_canEnterDayOfWeeks (dungeonrecordcli.cpp:371).
    public static bool OpenOnDay(IReadOnlyCollection<DayOfWeek> days, DayOfWeek today) =>
        days.Count == 0 || days.Contains(today);

    // A room's reward period. The client reads only cooldownValue and takes the type from it
    // (DungeonRoom::GetCooldownType, commonfiles/dungeonroom.cpp:524: 0 is the next day, else a weekday), so a
    // table without cooldownType (the KR one) still has weekly rooms; the ingest maps the type the same way.
    public static DungeonCooldownType CooldownType(int cooldownValue) =>
        cooldownValue == 0 ? DungeonCooldownType.nextDay : DungeonCooldownType.dayOfWeeks;

    // The period's end, CalcCooldownEndTime (dungeonroom.cpp:770): the next midnight for nextDay; for
    // dayOfWeeks the midnight of the next day numbered cooldownValue (Sunday 1, GetWeekdayStart in
    // timeutil.cpp:303), a week on when that day is today; no valid day (or no type) ends it at once.
    public static DateTime CooldownEnd(DungeonCooldownType type, int cooldownValue, DateTime clearTime) {
        switch (type) {
            case DungeonCooldownType.nextDay:
                return clearTime.Date.AddDays(1);
            case DungeonCooldownType.dayOfWeeks when cooldownValue is >= 1 and <= 7:
                int current = (int) clearTime.DayOfWeek;
                int target = cooldownValue - 1;
                if (current >= target) {
                    target += 7;
                }
                return clearTime.Date.AddDays(target - current);
            default:
                return clearTime;
        }
    }

    // Every time in a period has the same CooldownEnd, so a record still in its period has the expiry the rule
    // gives now. One that does not was written by an older rule: a weekday counted from Sunday 0 at the clear's
    // time of day, 1 to 7 days after the clear, kept until it passed. Its clear is taken at the earliest, a period
    // before that end, and the record gets the rule's end of that clear, its counts kept: the end of the week the
    // clear was in, or one already past, which a clear then starts again. A record past its expiry or a room
    // without a valid period is left.
    public static bool HealCooldown(DungeonRecord record, DungeonCooldownType type, int cooldownValue, DateTime now) {
        long nowStamp = new DateTimeOffset(now).ToUnixTimeSeconds();
        if (nowStamp >= record.UnionCooldownTimestamp) {
            return false;
        }
        DateTime end = CooldownEnd(type, cooldownValue, now);
        if (end <= now || new DateTimeOffset(end).ToUnixTimeSeconds() == record.UnionCooldownTimestamp) {
            return false;
        }
        int days = type == DungeonCooldownType.nextDay ? 1 : 7;
        DateTime written = DateTimeOffset.FromUnixTimeSeconds(record.UnionCooldownTimestamp).LocalDateTime;
        record.UnionCooldownTimestamp = new DateTimeOffset(CooldownEnd(type, cooldownValue, written.AddDays(-days))).ToUnixTimeSeconds();
        return true;
    }
}

using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// RoomDungeon 0x002B mode 2 (create): i32 dungeon id, bool with party, i32 portal id, bool use ticket.
// The client's room window enables the solo button only for a dungeon without a minimum user
// count (or in its debug state) and the party button only for a party in a dungeon that holds
// more than one user.
public static class DungeonRoomRules {
    // RoomDungeon mode 10 (UIDungeonClosingPhotoDialog::OnClosingPhotoPacket): the clear or fail banner, then
    // the closing photo of the room's players with their records, which opens the reward window when it
    // closes. The reward packet opens that window by itself, and KMS2 sends no mode 10 on a clear by a player
    // alone. Chosen here: a clear with more than one player, and a fail, keep it (KMS2 unknown for both).
    // players is the number of the room's players present at the end (the ones the photo lists), not the
    // party size: the client and KMS2 give no rule for a party of which one player is left.
    public static bool SendsClosingPhoto(DungeonState state, int players) {
        return state == DungeonState.Fail || (state == DungeonState.Clear && players > 1);
    }

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

    // The room a dungeon reward pouch (item function OpenDungeonItemBox, @0x140d24e70) names: MakeDungeonRoomId of
    // its two arguments (dungeonroom.cpp:251, dungeon * 1000 + index), each read as 0 when it is not a number.
    public static int PouchRoomId(string parameters) {
        string[] args = parameters.Split(',');
        int Arg(int index) => index < args.Length && int.TryParse(args[index].Trim(), out int value) ? value : 0;
        return Arg(0) * 1000 + Arg(1);
    }

    // The pouch opens while the reward room's record has a count left as the client counts it there: the counts
    // taken in the period (DungeonRecord::GetBonusRewardCount, 0 once now reaches the expire) below the room's
    // rewardCount plus the bought counts (DungeonRecordCli::GetMaxCount). The daily subRewardCount and the event
    // counts are not asked. A missing record has a count.
    public static bool PouchHasCountLeft(DungeonRecord? record, int rewardCount, long now) {
        int taken = record == null || now >= record.UnionCooldownTimestamp ? 0 : record.UnionClears;
        return taken < rewardCount + BonusCountNow(record, now);
    }

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

    // Whether characterId cleared the room as a rookie with a veteran: their own record is not a veteran's and
    // another player's of the clear is (70210090 "Complete a dungeon as a dungeon rookie with the aid of a dungeon veteran").
    public static bool WithOtherVeteran(long characterId, IReadOnlyDictionary<long, bool> veterans) =>
        veterans.TryGetValue(characterId, out bool veteran) && !veteran &&
        veterans.Any(entry => entry.Key != characterId && entry.Value);

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

    // isLimitTime: such a room is open only inside a range the running DungeonEnterTimeLimit events give it, both
    // ends included (CGameRule::IsEventTimeLimitDungeonOpen, commonfiles/gamerule.cpp:1552); with no range the
    // client's room page says it cannot be entered (uidungeonroomdialog.cpp:727). Tested on the server's local
    // time of day, as the day limit above. The refusal is OnError 0x15, s_room_dungeon_NotAllowTime.
    public static bool OpenAtTime(bool isLimitTime, int dungeonId, IEnumerable<DungeonEnterTime> times, TimeSpan timeOfDay) {
        if (!isLimitTime) {
            return true;
        }
        int seconds = (int) timeOfDay.TotalSeconds;
        return times.Any(time => time.DungeonId == dungeonId && time.StartSeconds <= seconds && seconds <= time.EndSeconds);
    }

    // openPeriodDate and openPeriodTag (DungeonRoom, commonfiles/dungeonroom.cpp:422). A room with a date still
    // ahead is closed, and only then the tag is not looked at; from the date on, and with no date, a room with a
    // tag is open only while the tag is open (OpenPeriodTags), as the client's room page tells it
    // (uidungeonroomdialog.cpp:752). The refusals are OnError 0x1c and 0x1d (dungeonrecordcli.cpp:419). Null when
    // the room is open.
    public static DungeonRoomError? OpenPeriodError(DateTime? openDate, string openTag, IEnumerable<string> runningTags, DateTime now) {
        if (openDate != null && now < openDate) {
            return DungeonRoomError.s_room_dungeon_is_not_open_period_date;
        }
        if (!string.IsNullOrEmpty(openTag) && !runningTags.Contains(openTag)) {
            return DungeonRoomError.s_room_dungeon_is_not_open_period;
        }
        return null;
    }

    // A DungeonOpenPeriod event: its id, tag and time span.
    public readonly record struct OpenPeriod(int Id, string Tag, DateTime Start, DateTime End);

    // The tags open at now for a client that has been in the game since `since`. The client keeps one period per
    // tag: an event that starts while another of its tag is kept is dropped for good ("TagID Duplicated",
    // GameEventDungeonOpenPeriod::OnStart, gameevent.cpp:1781), and the tag goes when the kept event ends, so the
    // dropped one never opens the room. periods: every event known since then, in event list order; those running
    // at `since` reach the client in that order, the later ones as they start.
    public static HashSet<string> OpenPeriodTags(IEnumerable<OpenPeriod> periods, DateTime since, DateTime now) {
        var kept = new Dictionary<string, OpenPeriod>();
        IEnumerable<OpenPeriod> ordered = periods
            .Where(period => period.Tag.Length > 0 && period.End >= since && period.Start <= now)
            .Select((period, index) => (period, index))
            .OrderBy(entry => entry.period.Start <= since ? since : entry.period.Start)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.period);
        foreach (OpenPeriod period in ordered) {
            if (kept.TryGetValue(period.Tag, out OpenPeriod keeper) && period.Start <= keeper.End) {
                continue;
            }
            kept[period.Tag] = period;
        }
        return kept.Values.Where(period => period.End >= now).Select(period => period.Tag).ToHashSet();
    }

    // The table's openPeriodDate, written as the event times are ("yyyy-MM-dd-HH-mm", seconds optional); null for
    // no date or one that does not read.
    public static DateTime? ParseOpenPeriodDate(string? text) {
        string[] formats = ["yyyy-MM-dd-HH-mm-ss", "yyyy-MM-dd-HH-mm", "yyyy-MM-dd-HH", "yyyy-MM-dd"];
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }
        return DateTime.TryParseExact(text.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateTime date) ? date : null;
    }

    // A room's reward period. The client reads only cooldownValue and takes the type from it
    // (DungeonRoom::GetCooldownType, commonfiles/dungeonroom.cpp:524: 0 is the next day, else a weekday), so a
    // table without cooldownType (the KR one) still has weekly rooms; the ingest maps the type the same way.
    // A Colosseum run continued in a new room starts with the rounds cleared before (its reward count); a round the
    // run has already cleared there is kept.
    public static int ContinuedRound(int round, int cleared) => Math.Max(round, cleared);

    // The Colosseum's round rewards: the reward ids of round entries from to to - 1, as DungeonRoom::GetRoundRewardIds
    // (commonfiles/dungeonroom.cpp:711) gives them through DungeonRoundGroup::GetRewardIds (:1013); round N is entry
    // N-1. A clear gives the rounds it adds to the period's count (from the rounds counted before it to the run's
    // round), so each round's reward is given once a period, as the room's reward count (one per round) allows.
    public static List<int> RoundRewardIds(IReadOnlyList<DungeonRoundMetadata> rounds, int from, int to) {
        List<int> ids = [];
        for (int i = Math.Max(from, 0); i < to && i < rounds.Count; i++) {
            if (rounds[i].RewardId > 0) {
                ids.Add(rounds[i].RewardId);
            }
        }
        return ids;
    }

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

    // The boxes a clear with a reward count left gives: the room's rewardLimitedDropBoxIds, the boxes the client's
    // reward preview lists (DungeonRoom::Load reads them into m_rewardDropBoxIds, dungeonroom.cpp:391-395), then its
    // rewardUnlimitedDropBoxIds. Box 0 is no box, and a box is given once when both lists name it (the KR client's
    // rows often list the boss box in both).
    public static IReadOnlyList<int> ClearDropBoxIds(DungeonRoomRewardMetadata reward) {
        return reward.LimitedDropBoxIds.Concat(reward.UnlimitedDropBoxIds).Where(boxId => boxId != 0).Distinct().ToList();
    }

    // The united weekly reward of a clear: the step (UnitedWeeklyRewards: clear count to reward id) of the count the
    // clear reached, only when the clear raised the union record's count. A clear past the daily or weekly cap (or
    // one that took an event count) leaves the count where it is, and gives no step again.
    public static int? UnitedWeeklyStep(IReadOnlyDictionary<int, int> steps, int countBefore, int countAfter) =>
        countAfter > countBefore && steps.TryGetValue(countAfter, out int rewardId) ? rewardId : null;

    // The weekly reset zeroes a record's period counts only when its own period is over (the record's
    // UnionCooldownTimestamp, the day the room's cooldownValue names, which the client counts down to), as
    // RewardCount reads them; a record still in its period keeps them.
    public static bool PeriodOver(DungeonRecord record, long now) => now > record.UnionCooldownTimestamp;
}

using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

public static class GuildRules {
    // give_guild_exp type: the id of a GuildGainExp table the data does not ship. The trigger scripts' own note
    // on the action says 2 is the massive event, and boxID 0 is the whole map. Every use is type 2: the massive
    // event maps 61000001-61000022 when the game starts, the individual arenas Red Arena 65000001, Crimson Tear Mine
    // 65000002 and Treasure Island 65000003, and the wedding mini game room 84000007_wd.
    public const int MassiveEventGainExp = 2;

    // What a massive event pays a guild with a member in it, from the row of the guild's level: the mini game
    // rows of guildproperty.xml, the lose (taking part) ones, as the action runs when the game starts, before
    // anyone wins. The pay is chosen here (no GuildGainExp table), so it must not be farmable: only a field of an
    // instancefield row of type massiveEvent pays (the 61000xxx maps, opened by the schedule). The arenas, which a
    // player queues for again at once, and the wedding room have no such row and pay nothing; so does any type that
    // is not the massive event.
    public static (int Exp, int Fund) GainExpFund(GuildTable.Property? property, int type, InstanceType? fieldType) {
        if (property == null || type != MassiveEventGainExp || fieldType != InstanceType.massiveEvent) {
            return (0, 0);
        }
        return (property.LoseMiniGameExp, property.LoseMiniGameFund);
    }

    // A guild name a new guild or a rename takes (the ban words are checked apart): not blank, min to max letters
    // (the constants GuildNameLengthMin and GuildNameLengthMax) and the letters a name may have.
    public static bool IsGuildName(string name, int min, int max) {
        return !string.IsNullOrWhiteSpace(name) && name.Length >= min && name.Length <= max && NameValidator.ValidName(name);
    }

    // Rank 0 is the master's rank.
    public const byte MasterRank = 0;

    // Guild 10 (set a member's rank): the client sends a rank from 1 to the last rank index and never 0.
    // Rank 0 moves only with the master change (Guild 63).
    public static bool IsAssignableRank(int rank, int rankCount) {
        return rank > MasterRank && rank < rankCount;
    }

    // Guild 64 (set a guild text) carries a kind byte before the text. Kind 1 is the guild notice, written by a
    // member with the notice permission, at most 300 characters. Kind 0 is a second text of at most 1000
    // characters that only the master may set; the 2525 client has no caller for it and the server keeps no such
    // text, so only kind 1 is stored.
    public const byte NoticeKind = 1;
    public const int MaxNoticeLength = 300;

    public static bool IsNotice(byte kind, string text) {
        return kind == NoticeKind && text.Length <= MaxNoticeLength;
    }

    // Guild 68 (CGuildManager::SendGuildMode68): a byte, listed in the guild search or not, then the focus bits.
    // The guild window sets the 16 bits of GuildFocus.
    public const int FocusMask = 0xFFFF;

    public static bool IsFocus(byte listed, int focus) {
        return listed <= 1 && (focus & ~FocusMask) == 0;
    }

    // Guild 87 (CGuildManager::SendGuildMode87): the focus bits to look for, or all 31 bits when the player
    // ticked none. Guild 88 (SendGuildMode88): a name of 2 characters or more.
    public const int SearchAnyFocus = 0x7FFFFFFF;
    public const int MinSearchNameLength = 2;
    // The client reads any count and pages by 9. The cap is the server's own.
    public const int MaxSearchResults = 90;

    public static bool IsSearchFocus(int focus) {
        return focus == SearchAnyFocus || (focus != 0 && (focus & ~FocusMask) == 0);
    }

    public static bool IsSearchName(string name) {
        return name.Length >= MinSearchNameLength && name.Length <= 50;
    }

    // Guild 82 (CGuildManager::SendGuildMode82): a player without a guild asks to join one. The client refuses
    // to send with 10 requests open and shows error 37 itself; the server holds the same limit.
    public const int MaxOpenApplications = 10;

    public static bool CanApply(int openApplications) {
        return openApplications < MaxOpenApplications;
    }

    // Guild 82 on the World server: the guild must be listed in the search, the player has no guild, no request
    // to this guild yet and fewer than 10 requests open.
    public static GuildError CheckApply(bool guildListed, long currentGuildId, IEnumerable<GuildApplicationRow> own, long guildId) {
        if (!guildListed) {
            return GuildError.s_guild_err_null_guild;
        }
        if (currentGuildId != 0) {
            return GuildError.s_guild_err_has_guild;
        }
        int open = 0;
        foreach (GuildApplicationRow row in own) {
            if (row.GuildId == guildId) {
                return GuildError.s_guild_search_last_request;
            }
            open++;
        }
        return CanApply(open) ? GuildError.none : GuildError.s_guild_search_max_join_request;
    }

    // Guild 84 on the World server: the sender is a member of the guild named in the call and holds the invite
    // right; the request is a row of that guild for that applicant; an applicant who has a guild cannot be accepted.
    public static GuildError CheckRespond(bool isMember, bool hasInviteRight, long guildId, long applicantId,
                                          GuildApplicationRow? row, bool accepted, long applicantGuildId) {
        if (!isMember) {
            return GuildError.s_guild_err_null_member;
        }
        if (!hasInviteRight) {
            return GuildError.s_guild_err_no_authority;
        }
        if (row == null || row.GuildId != guildId || row.ApplicantId != applicantId) {
            return GuildError.s_guild_search_null_join_guild_request;
        }
        if (accepted && applicantGuildId != 0) {
            return GuildError.s_guild_err_has_guild;
        }
        return GuildError.none;
    }

    // Guild 83 (SendGuildMode83) carries one id. The client code names it the guild id; the join page hands the
    // script both the guild id and the request id of a row, and which of the two the script passes back is not
    // in the client code. So the id is matched against the player's own requests only: first as the guild,
    // then as the request.
    public static GuildApplicationRow? FindOwnApplication(IEnumerable<GuildApplicationRow> own, long id) {
        GuildApplicationRow? byRequest = null;
        foreach (GuildApplicationRow row in own) {
            if (row.GuildId == id) {
                return row;
            }
            if (row.Id == id) {
                byRequest = row;
            }
        }
        return byRequest;
    }

    // Guild 71 (CGuildManager::SendGuildMail): one mail to every member but the sender.
    public static IList<long> MailReceivers(IEnumerable<long> memberIds, long senderId) {
        return memberIds.Where(id => id != senderId).Distinct().OrderBy(id => id).ToList();
    }

    // A server rule, not in the client (which sends 71 without a wait): one guild mail per sender in 60
    // seconds, because one packet writes a mail for every other member.
    public const int MailCooldownSeconds = 60;

    // What the sender is told inside the 60 seconds. The client has no text of its own for a mail sent too
    // soon; CGuildManager::ShowGuildError (guildmanager.cpp:3230) shows "Please try again later." for 45
    // (:3351) and "Unknown Guild Error" for 1 (:3234).
    public const GuildError MailCooldownError = GuildError.s_guild_err_too_fast_to_search_by_name;

    public static bool CanSendMail(long now, long lastSent) {
        return lastSent <= 0 || now < lastSent || now - lastSent >= MailCooldownSeconds;
    }

    // The limit over a table of the last mail time of each sender. A sender inside the 60 seconds is
    // refused and the table stays as it is; else the entries that are no longer held back are removed (so
    // the table holds at most the senders of the last 60 seconds) and the sender's time is written.
    public static bool TakeMailTurn(IDictionary<long, long> lastMail, long senderId, long now) {
        lastMail.TryGetValue(senderId, out long last);
        if (!CanSendMail(now, last)) {
            return false;
        }
        foreach (long id in lastMail.Where(entry => CanSendMail(now, entry.Value)).Select(entry => entry.Key).ToList()) {
            lastMail.Remove(id);
        }
        lastMail[senderId] = now;
        return true;
    }

    // Guild 15 (CGuildManager::SendGuildMode15, guildmanager.cpp:617): the client sends a check-in only from
    // the day after the member joined (s_guild_err_can_next_day, :623) and once a day
    // (s_guild_err_attend_already, :627). Both texts are the client's own; the error packet has no code for
    // them. The day is the client's (DayStart), as for the donation.
    public static bool CanCheckIn(long joinTime, long checkinTime, long dayStart) {
        return joinTime < dayStart && checkinTime < dayStart;
    }

    // The start of the day `now` falls in, in `zone`. The client's own day test (IsNotRecentDayChange,
    // timeutil.cpp:344) compares CMsTime dates, which run in the zone the server sends with TimeSync, the
    // server's local zone; the daily reset runs at its midnight too. A UTC day refused a check-in or kept the
    // donation count from the client's day change until UTC midnight.
    public static long DayStart(long now, TimeZoneInfo zone) {
        DateTime day = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(now).UtcDateTime, zone).Date;
        return new DateTimeOffset(day, zone.GetUtcOffset(day)).ToUnixTimeSeconds();
    }

    public static long DayStart() => DayStart(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TimeZoneInfo.Local);

    // Guild 113 (CGuildManager::FUN_1413b8540): the count of donations made at once. The client refuses a
    // count under 1, a member who joined today, more donations in a day than the guild level allows, and a
    // donation that takes the guild fund over its maximum. The count of an earlier day does not carry over.
    public static int DonationsToday(int dailyCount, long donationTime, long dayStart) {
        return donationTime >= dayStart ? dailyCount : 0;
    }

    public static bool CanDonate(int count, long joinTime, long dayStart, int donationsToday, int donateMax,
                                 long funds, long addFunds, long fundMax) {
        if (count <= 0 || addFunds < 0 || joinTime >= dayStart) {
            return false;
        }
        return (long) donationsToday + count <= donateMax && funds + addFunds <= fundMax;
    }

    // A guild quest's guildFund added to the fund: it stops at the level's fundMax (and at int.MaxValue, the
    // fund's width); a fund already over the ceiling is not cut down.
    public static int AddFund(int funds, int addFunds, long fundMax) {
        if (addFunds <= 0) {
            return funds;
        }
        long ceiling = Math.Min(fundMax, int.MaxValue);
        if (funds >= ceiling) {
            return funds;
        }
        return (int) Math.Min((long) funds + addFunds, ceiling);
    }

    // A guild shop buy from the guild fund: the master only (shop error 26), and the fund must cover the price (27).
    public static GuildError CheckSpendFund(bool isMaster, int funds, int amount) {
        if (!isMaster) {
            return GuildError.s_guild_err_no_master;
        }
        if (amount < 0 || amount > funds) {
            return GuildError.s_guild_err_not_enough_guild_fund;
        }
        return GuildError.none;
    }

    // Guild 92 (CGuildManager::FUN_1413b9ac0): the master raises a guild skill by one level. The client asks
    // for the row of the current level and of the next one (none: s_guild_err_max_level, which has no code in
    // the error packet), the guild level of the next row, and the upgrade cost of the next row in the fund.
    // Skills 1001 to 10000 are paid from the event fund (GuildSkill::FUN_1404d4e80), which the server does
    // not keep: for them the fund counts as 0.
    // Guild 101 (CGuildManager::FUN_1413ba820), in the client's order: the house row of the next level and
    // the chosen theme must exist, the master asks, the guild level reaches the row's, and the fund pays.
    public static GuildError CheckUpgradeHouse(bool isMaster, GuildTable.House? next, int guildLevel, int funds) {
        if (next == null) {
            return GuildError.s_guild_err_unknown;
        }
        if (!isMaster) {
            return GuildError.s_guild_err_no_master;
        }
        if (next.RequireLevel > guildLevel) {
            return GuildError.s_guild_err_not_enough_guild_level;
        }
        if (next.UpgradeCost < 0 || next.UpgradeCost > funds) {
            return GuildError.s_guild_err_not_enough_guild_fund;
        }
        return GuildError.none;
    }

    // Guild 102 (CGuildManager::FUN_1413babf0), in the client's order: the house row of the guild's own level
    // and the new theme must exist, the master asks, the theme is another one than today's (the client's
    // "same theme" text has no code in the error packet), and the fund pays the row's change cost.
    public static GuildError CheckChangeHouseTheme(bool isMaster, GuildTable.House? house, int currentTheme, int theme, int funds) {
        if (house == null) {
            return GuildError.s_guild_err_unknown;
        }
        if (!isMaster) {
            return GuildError.s_guild_err_no_master;
        }
        if (currentTheme == theme) {
            return GuildError.s_guild_err_unknown;
        }
        if (house.ReThemeCost < 0 || house.ReThemeCost > funds) {
            return GuildError.s_guild_err_not_enough_guild_fund;
        }
        return GuildError.none;
    }

    // Guild 114 (CGuildManager::FUN_1413bb000), in the client's order: the row of the next level must
    // exist ("highest level", which has no code in the error packet), the master asks, the guild level and
    // the house level reach the row's (the client shows the guild level text for both), and the fund pays.
    // A kind the guild does not have yet is at level 0, so its first upgrade asks for the row of level 1.
    public static GuildError CheckUpgradeNpc(bool isMaster, GuildTable.Npc? next, int guildLevel, int houseLevel, int funds) {
        if (next == null) {
            return GuildError.s_guild_err_unknown;
        }
        if (!isMaster) {
            return GuildError.s_guild_err_no_master;
        }
        if (next.RequireGuildLevel > guildLevel || next.RequireHouseLevel > houseLevel) {
            return GuildError.s_guild_err_not_enough_guild_level;
        }
        if (next.UpgradeCost < 0 || next.UpgradeCost > funds) {
            return GuildError.s_guild_err_not_enough_guild_fund;
        }
        return GuildError.none;
    }

    public static bool IsEventFundSkill(int skillId) {
        return skillId is > 1000 and <= 10000;
    }

    public static GuildError CheckUpgradeSkill(bool isMaster, int skillId, GuildTable.Buff? current, GuildTable.Buff? next,
                                               int guildLevel, int funds) {
        if (current == null || next == null) {
            return GuildError.s_guild_err_unknown;
        }
        if (!isMaster) {
            return GuildError.s_guild_err_no_master;
        }
        if (next.RequireLevel > guildLevel) {
            return GuildError.s_guild_err_not_enough_guild_level;
        }
        // The event fund is not kept by the server: such a skill is refused whatever its cost.
        if (IsEventFundSkill(skillId) || next.UpgradeCost < 0 || next.UpgradeCost > funds) {
            return GuildError.s_guild_err_not_enough_guild_fund;
        }
        return GuildError.none;
    }

    // Guild 90 (CGuildManager::FUN_1413b8fe0), in the client's order: the master asks, the row of the skill's
    // level must exist, its requireLevel must not be above the guild level, the fund pays its cost, and the
    // skill is neither running nor cooling down (CGuildSkill::IsUsable: the time of use plus the duration,
    // and the time of use plus the cooltime, are both over). The skills
    // above 10000 are the personal ones (Guild 91) and are refused here.
    public static GuildError CheckUseSkill(bool isMaster, int skillId, GuildTable.Buff? row, int guildLevel, int funds,
                                           long useTime, long now) {
        if (!isMaster) {
            return GuildError.s_guild_err_no_master;
        }
        // A row without a duration is never "running": it could be paid for again at once and would leave
        // no effect. The table has no such row for a guild skill, so it is refused.
        if (row == null || skillId > 10000 || row.Duration <= 0) {
            return GuildError.s_guild_err_unknown;
        }
        if (row.RequireLevel > guildLevel) {
            return GuildError.s_guild_err_not_enough_guild_level;
        }
        if (IsEventFundSkill(skillId) || row.Cost < 0 || row.Cost > funds) {
            return GuildError.s_guild_err_not_enough_guild_fund;
        }
        if (SkillRemainingSeconds(row, useTime, now) > 0 || SkillCooldownSeconds(row, useTime, now) > 0) {
            return GuildError.s_guild_err_cannot_use_skill;
        }
        return GuildError.none;
    }

    // CGuildCustomEvent::IsActive (guilddef_1.cpp:957): an event is open for this long after its time.
    public const int EventOpenSeconds = 30;
    // CGuildManager::FUN_1413ba080 (guildmanager.cpp:857): no event on the guild's first day.
    public const int EventGuildAgeSeconds = 86400;

    public static bool IsEventActive(long openTime, long now) {
        return openTime > 0 && openTime + EventOpenSeconds > now;
    }

    // Guild 99 (CGuildManager::FUN_1413ba080, guildmanager.cpp:857), in the client's order: the event has a
    // row, the guild is a day old, the member's rank has the mini game right, and the event is not open.
    public static GuildError CheckOpenEvent(bool hasRow, long creationTime, bool hasRight, long openTime, long now) {
        if (!hasRow) {
            return GuildError.s_guild_err_unknown;
        }
        if (creationTime + EventGuildAgeSeconds > now) {
            return GuildError.s_guild_err_unknown; // the client's own text is s_guild_err_cannot_open_event_create_date
        }
        if (!hasRight) {
            return GuildError.s_guild_err_no_authority;
        }
        if (IsEventActive(openTime, now)) {
            return GuildError.s_guild_err_unknown;
        }
        return GuildError.none;
    }

    // Guild 100 (CGuildManager::SendGuildMode100, guildmanager.cpp:881), in the client's order: the event
    // has a row with a map, it is open, and the member is not in its map already.
    public static bool CanEnterEvent(int eventMapId, long openTime, long now, int currentMapId) {
        return eventMapId > 0 && IsEventActive(openTime, now) && currentMapId != eventMapId;
    }

    // Guild 100 and the guild house entry move the member to another field. A dead member, or one inside
    // a dungeon or a home, stays where they are: the move would take them out of the death, the dungeon
    // room or the home without that field's own way out.
    public static bool CanGoToGuildField(bool dead, bool inDungeon, bool inHome) {
        return !dead && !inDungeon && !inHome;
    }

    // Guild 91 (CGuildManager::FUN_1413b96e0), in the client's order: the row of the skill's level must
    // exist and be a personal skill (above 10000), its requireLevel must not be above the guild level, the
    // member's meso pays its cost, and the member does not have the effect already. A row without an effect
    // would take the meso and give nothing, so it is refused.
    public static GuildError CheckUsePersonalSkill(int skillId, GuildTable.Buff? row, int guildLevel, long meso, bool hasEffect) {
        if (row == null || skillId <= 10000 || row.Duration <= 0 || row.Id <= 0) {
            return GuildError.s_guild_err_unknown;
        }
        if (row.RequireLevel > guildLevel) {
            return GuildError.s_guild_err_not_enough_guild_level;
        }
        if (row.Cost < 0 || row.Cost > meso) {
            return GuildError.s_guild_err_no_money;
        }
        if (hasEffect) {
            return GuildError.s_guild_err_cannot_use_skill;
        }
        return GuildError.none;
    }

    // A member who enters a PvP map loses every effect that is not marked to be kept there
    // (BuffManager.EnterField); the guild skill effects given on entry follow the same rule.
    public static bool GivesEffectOnEntry(bool isPvpMap, bool keepOnEnterPvpZone, bool removeOnPvpZone) {
        return !isPvpMap || (keepOnEnterPvpZone && !removeOnPvpZone);
    }

    // The effects a guild's skills can give, guild and personal ones, at the level the guild has them.
    public static List<(int Id, short Level)> SkillEffects(IEnumerable<GuildBuff> skills,
                                                           IReadOnlyDictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> table) {
        var result = new List<(int, short)>();
        foreach (GuildBuff skill in skills) {
            if (table.TryGetValue(skill.Id, out IReadOnlyDictionary<short, GuildTable.Buff>? levels)
                && levels.TryGetValue((short) skill.Level, out GuildTable.Buff? row) && row.Id > 0) {
                result.Add((row.Id, row.Level));
            }
        }
        return result;
    }

    // Guild 91: the meso a personal skill takes from the member. Nothing is taken when the use is refused.
    public static GuildError PersonalSkillCharge(int skillId, GuildTable.Buff? row, int guildLevel, long meso, bool hasEffect, out long charge) {
        GuildError error = CheckUsePersonalSkill(skillId, row, guildLevel, meso, hasEffect);
        charge = error == GuildError.none ? row!.Cost : 0;
        return error;
    }

    // The guild skills (ids up to 10000) that are running at `now`, one entry per skill, with the seconds
    // they have left. A skill past its end, never used, or without a row or an effect gives no entry.
    public static List<(int Id, short Level, long RemainingSeconds)> RunningSkillEffects(IEnumerable<GuildBuff> skills,
            IReadOnlyDictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> table, long now) {
        var result = new List<(int, short, long)>();
        foreach (GuildBuff skill in skills) {
            if (skill.Id > 10000 || !table.TryGetValue(skill.Id, out IReadOnlyDictionary<short, GuildTable.Buff>? levels)
                                 || !levels.TryGetValue((short) skill.Level, out GuildTable.Buff? row) || row.Id <= 0) {
                continue;
            }

            long remaining = SkillRemainingSeconds(row, skill.ExpiryTime, now);
            if (remaining > 0) {
                result.Add((row.Id, row.Level, remaining));
            }
        }
        return result;
    }

    // Guild 109 (CGuildManager::FUN_1413bb490, guildmanager.cpp:1046): the client's own tests before it
    // sends, made again: a count, the storage holds it, and a receiver in the guild.
    public static GuildError CheckStorageGift(int count, int stored, bool receiverIsMember) {
        if (!receiverIsMember) {
            return GuildError.s_guild_err_null_member;
        }
        if (count <= 0 || stored < count) {
            return GuildError.s_guild_err_unknown;
        }
        return GuildError.none;
    }

    // Guild 115 (CGuildManager::SendGuildMode115, guildmanager.cpp:1425): the mini game right, and a time
    // when the vs-game takes applications. The server runs no vs-game, so `isVsGameTime` is false there.
    public static GuildError CheckVsGameRegister(bool hasMiniGameRight, bool isVsGameTime) {
        if (!hasMiniGameRight) {
            return GuildError.s_guild_err_no_authority;
        }
        return isVsGameTime ? GuildError.none : GuildError.s_guild_err_isNotVsGameTime;
    }

    // CGuildSkill::GetRemainingDuration: the table's duration is in minutes, the times are in seconds (the
    // client's table reader GuildSkill::FUN_1404d4cb0 stores duration * 60 and cooltime * 60, and the
    // getters add them to the time of use). A skill that was never used has the time 0.
    public static long SkillRemainingSeconds(GuildTable.Buff row, long useTime, long now) {
        if (useTime == 0) {
            return 0;
        }
        return Math.Max(0, useTime + row.Duration * 60L - now);
    }

    // CGuildSkill::GetRemainingCooldown: the table's cooltime is in minutes too, counted from the time of use.
    public static long SkillCooldownSeconds(GuildTable.Buff row, long useTime, long now) {
        return Math.Max(0, useTime + row.Cooldown * 60L - now);
    }

    // The master keeps rank 0 until the master changes.
    public static bool CanChangeRankOf(byte memberRank) {
        return memberRank != MasterRank;
    }

    // guild_exp ("Obtain 100 guild experience", 70300016): the guild exp a member's own action added. Every
    // member gets the new total; only the contributor counts the rise, and a total that did not rise counts nothing.
    public static long ContributedExp(long characterId, long contributorId, int oldExp, int newExp) {
        return characterId == contributorId && newExp > oldExp ? newExp - oldExp : 0;
    }

    // The guild bonus of a guild quest's reward (FUN_140762500 and its siblings, questmasktable.cpp:915): the factor of
    // the guild's quest npc level for the reward kind (guildquestrewardfactor.xml); a factor below 1 (no quest npc, a
    // level the table lacks) gives no bonus. The reward line shows, and the server gives, value * (1 + (factor - 1))
    // in float (FUN_140761ba0).
    public static long QuestRewardBonus(long value, float factor) {
        float bonus = factor >= 1.0f ? factor - 1.0f : 0f;
        return (long) ((float) value * (1.0f + bonus));
    }

    // The weekly reset: each member's weekly contribution goes back to 0 (the total stays), under the member's
    // lock as a check-in or a donation adds to it. Returns the members that had any.
    public static int ResetWeeklyContribution(IEnumerable<GuildMember> members) {
        int reset = 0;
        foreach (GuildMember member in members) {
            lock (member) {
                if (member.WeeklyContribution != 0) {
                    reset++;
                }
                member.WeeklyContribution = 0;
            }
        }
        return reset;
    }

    // The guild.xml row of the level an experience gives, as CGuildXmlData::GetLevelByExp picks it: the lowest
    // level whose experience is above the guild's (a row's experience is what the next level needs), else the
    // last level. The row carries the level's capacity, fund cap and check-in rewards.
    // The cap a guild has without a guild.xml row (a level 1 guild's).
    public const byte DefaultCapacity = 60;

    public static GuildTable.Property? PropertyAt(IReadOnlyDictionary<short, GuildTable.Property> properties, int experience) {
        GuildTable.Property? last = null;
        foreach (GuildTable.Property property in properties.Values.OrderBy(entry => entry.Level)) {
            if (experience < property.Experience) {
                return property;
            }
            last = property;
        }
        return last;
    }
}

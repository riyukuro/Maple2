using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

public static class GuildRules {
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

    // Guild 91 (CGuildManager::FUN_1413b96e0), in the client's order: the row of the skill's level must
    // exist and be a personal skill (above 10000), its requireLevel must not be above the guild level, the
    // member's meso pays its cost, and the member does not have the effect already.
    public static GuildError CheckUsePersonalSkill(int skillId, GuildTable.Buff? row, int guildLevel, long meso, bool hasEffect) {
        if (row == null || skillId <= 10000 || row.Duration <= 0) {
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
}

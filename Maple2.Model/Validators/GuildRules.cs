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
        if (next.UpgradeCost < 0 || next.UpgradeCost > (IsEventFundSkill(skillId) ? 0 : funds)) {
            return GuildError.s_guild_err_not_enough_guild_fund;
        }
        return GuildError.none;
    }

    // The master keeps rank 0 until the master changes.
    public static bool CanChangeRankOf(byte memberRank) {
        return memberRank != MasterRank;
    }
}

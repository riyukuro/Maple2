using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class GuildPacket {
    private enum Command : byte {
        Load = 0,
        Created = 1,
        Disbanded = 2,
        Invited = 3,
        InviteInfo = 4,
        InviteReply = 5,
        NotifyInvite = 6,
        Leave = 7,
        Expelled = 8,
        NotifyExpel = 9,
        UpdateMemberRank = 10,
        UpdateMemberMessage = 11,
        UpdateMemberName = 12,
        UpdateMemberGearScore = 14,
        CheckedIn = 15,
        Joined = 18,
        NotifyLeave = 19,
        NotifyExpelMember = 20,
        NotifyUpdateMemberRank = 21,
        NotifyUpdateMemberMessage = 22,
        NotifyLogin = 23,
        NotifyLogout = 24,
        NotifyUpdateLeader = 25,
        NotifyUpdateNotice = 26,
        NotifyUpdateEmblem = 27,
        NotifyUpdateCapacity = 28,
        NotifyUpdateRank = 29,
        NotifyUpdateFocus = 30,
        UpdateMemberMap = 31,
        UpdateMember = 32,
        // NotifyLeave = 33, // Duplicate of 19?
        UpdateName = 34,
        Achievement = 35,
        CheckInTime = 36,
        MemberRaid = 37,
        PvpMemberCounts = 38,
        PvpState = 39,
        PvpMatched = 40,
        PvpRating = 41,
        PvpResult = 42,
        PvpRegistered = 43,
        PvpUnregistered = 44,
        ReceiveApplication = 45,
        WithdrawApplication = 46,
        NotifyApplication = 47,
        NotifyApplicant = 48,
        GuildExperience = 50,
        GuildFunds = 51,
        GuildContribution = 53,
        NotifyUseBuff = 54,
        UpgradeBuff = 55,
        NotifyOpenEvent = 56,
        UpgradeHouse = 57,
        UpdatePoster = 58,
        UpgradeNpc = 59,
        WeeklyContributionReset = 61,
        UpdateLeader = 63,
        UpdateNotice = 64,
        UpdateEmblem = 65,
        UpdateCapacity = 66,
        UpdateRank = 67,
        UpdateFocus = 68,
        SendMail = 71,
        PvpHistory = 74,
        PvpSeason = 75,
        ExpelledOffline = 76,
        AddTag = 77,
        RemoveTag = 78,
        Error = 79,
        // 78
        SendApplication = 82,
        CancelApplication = 83,
        RespondApplication = 84,
        ListApplications = 85,
        ListAppliedGuilds = 86,
        ListGuilds = 87,
        UseBuff = 90,
        UsePersonalBuff = 91,
        AddContribution = 97,
        OpenEvent = 99,
        // 96, 104, 107
        StorageGift = 109,
        StorageItem = 110,
        StorageHistory = 112,
        Donated = 113, // client CGuildManager::OnPacketMyDonation (case 0x71)
        VsGameMatchCanceled = 117,
        VsGameTicket = 118,
        VsGameOpen = 119,
        VsGameResult = 122,
        VsGameReward = 123,
        // 114, 115, 116, 120
    }

    public static ByteWriter Load(Guild guild) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteClass<Guild>(guild);

        return pWriter;
    }

    public static ByteWriter Created(string guildName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Created);
        pWriter.Write<GuildError>(GuildError.none);
        pWriter.WriteUnicodeString(guildName);

        return pWriter;
    }

    public static ByteWriter Disbanded() {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Disbanded);
        pWriter.Write<GuildError>(GuildError.none);

        return pWriter;
    }

    public static ByteWriter Invited(string playerName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Invited);
        pWriter.WriteUnicodeString(playerName);

        return pWriter;
    }

    public static ByteWriter InviteInfo(GuildInvite info) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.InviteInfo);
        pWriter.WriteClass<GuildInvite>(info);

        return pWriter;
    }

    // s_guild_join
    // - You joined the guild {0}.
    // s_guild_join_reject
    // - You have rejected the guild invitation from {0}.
    public static ByteWriter InviteReply(GuildInvite info, bool accept) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.InviteReply);
        pWriter.WriteClass<GuildInvite>(info);
        pWriter.WriteBool(accept);

        return pWriter;
    }

    public static ByteWriter NotifyInvite(string name, GuildInvite.Response response) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyInvite);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteBool(response == GuildInvite.Response.Accept);
        pWriter.Write<GuildInvite.Response>(response);

        return pWriter;
    }

    // s_guild_leave
    // - You have left {0}.
    public static ByteWriter Leave() {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Leave);

        return pWriter;
    }

    public static ByteWriter Expelled(string playerName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Expelled);
        pWriter.WriteUnicodeString(playerName);

        return pWriter;
    }

    // s_guild_notify_expeled_from
    // - {0} has kicked you out of the guild.
    // s_guild_notify_expeled
    // - You have been kicked out of the guild.
    public static ByteWriter NotifyExpel(string playerName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyExpel);
        pWriter.WriteUnicodeString(playerName);

        return pWriter;
    }

    // Guild 76 at login, for the expels that came while the player was offline. The client shows
    // s_guild_notify_expeled_from ({0} has kicked you out of the guild.) for each entry whose reason is "expeled".
    public static ByteWriter ExpelledOffline(IList<GuildExpelNotice> notices) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.ExpelledOffline);
        pWriter.WriteInt(notices.Count);
        foreach (GuildExpelNotice notice in notices) {
            pWriter.WriteUnicodeString(notice.Name);
            pWriter.WriteUnicodeString("expeled");
            pWriter.WriteLong(notice.Time);
        }

        return pWriter;
    }

    public static ByteWriter UpdateMemberRank(string playerName, byte rankId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateMemberRank);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteByte(rankId);

        return pWriter;
    }

    public static ByteWriter UpdateMemberMessage(string message) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateMemberMessage);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    public static ByteWriter UpdateMemberName(string oldName, string newName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateMemberName);
        pWriter.WriteUnicodeString(oldName);
        pWriter.WriteUnicodeString(newName);

        return pWriter;
    }

    // s_guild_attend_complete
    // - You checked in.
    public static ByteWriter CheckedIn() {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.CheckedIn);

        return pWriter;
    }

    // s_guild_notify_accept_invite
    // - {0} invited {1} to the guild, and they accepted.
    public static ByteWriter Joined(string requestorName, GuildMember member, bool notify = true) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Joined);
        pWriter.WriteUnicodeString(requestorName);
        pWriter.WriteUnicodeString(member.Name);
        pWriter.WriteBool(notify);
        pWriter.WriteClass<GuildMember>(member);

        return pWriter;
    }

    // s_guild_notify_leave
    // - {0} has left the guild.
    public static ByteWriter NotifyLeave(string playerName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyLeave);
        pWriter.WriteUnicodeString(playerName);

        return pWriter;
    }

    // s_guild_notify_expel_member
    // - {0} has kicked {1} out of the guild.
    public static ByteWriter NotifyExpelMember(string requestorName, string playerName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyExpelMember);
        pWriter.WriteUnicodeString(requestorName);
        pWriter.WriteUnicodeString(playerName);

        return pWriter;
    }

    // s_guild_notify_change_member_grade
    // - {0} is now the rank of [{1}].
    // s_guild_notify_change_member_grade_me
    // - Updated to the rank of [{0}] in the guild.
    public static ByteWriter NotifyUpdateMemberRank(string requestorName, string playerName, byte rankId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateMemberRank);
        pWriter.WriteUnicodeString(requestorName);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteByte(rankId);

        return pWriter;
    }

    public static ByteWriter NotifyUpdateMemberMessage(GuildMember member) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateMemberMessage);
        pWriter.WriteUnicodeString(member.Name);
        pWriter.WriteUnicodeString(member.Message);

        return pWriter;
    }

    // s_guild_notify_login_member
    // - Guild member {0} has logged on.
    public static ByteWriter NotifyLogin(string name) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyLogin);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // s_guild_notify_logout_member
    // - Guild member {0} has logged off.
    public static ByteWriter NotifyLogout(string name, long time) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyLogout);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteLong(time);

        return pWriter;
    }

    // s_guild_notify_change_master
    // - {0} is now the guild leader.
    // s_guild_notify_change_master_me
    // - You have become the guild leader.
    public static ByteWriter NotifyUpdateLeader(string oldLeader, string newLeader) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateLeader);
        pWriter.WriteUnicodeString(oldLeader);
        pWriter.WriteUnicodeString(newLeader);

        return pWriter;
    }

    // s_guild_notify_change_notify
    // - {0} has changed the guild notice.
    public static ByteWriter NotifyUpdateNotice(string requestorName, byte notice, string message) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateNotice);
        pWriter.WriteUnicodeString(requestorName);
        pWriter.WriteByte(notice); // 1
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    // s_guild_notify_change_mark
    // - {0} has changed the guild emblem.
    // The client puts the text it reads into that line itself (CGuildManager::FUN_1413c15b0), so the text is
    // the changer's name alone, as KMS2 sends it; the line's own code here would wrap the line in itself.
    public static ByteWriter NotifyUpdateEmblem(string requestorName, string emblem) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateEmblem);
        pWriter.WriteClass<InterfaceText>(new InterfaceText(requestorName));
        pWriter.WriteUnicodeString(emblem);

        return pWriter;
    }

    // s_guild_notify_change_capacity
    // - The guild's member maximum has been changed.
    public static ByteWriter NotifyUpdateCapacity(string requestorName, int capacity) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateCapacity);
        pWriter.WriteUnicodeString(requestorName);
        pWriter.WriteInt(capacity);

        return pWriter;
    }

    // s_guild_notify_change_grade
    // - {0}'s guild rank and authority have changed.
    public static ByteWriter NotifyUpdateRank(InterfaceText text, GuildRank rank) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateRank);
        pWriter.WriteClass<InterfaceText>(text);
        pWriter.WriteByte(rank.Id);
        pWriter.WriteClass<GuildRank>(rank);

        return pWriter;
    }

    public static ByteWriter NotifyUpdateFocus(string requestorName, bool listed, GuildFocus focus) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUpdateFocus);
        pWriter.WriteUnicodeString(requestorName);
        pWriter.WriteBool(listed);
        pWriter.WriteInt((int) focus);

        return pWriter;
    }

    public static ByteWriter UpdateMemberMap(string playerName, int mapId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateMemberMap);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteInt(mapId);

        return pWriter;
    }

    // The client stores the value as the named member's gear score (CGuildManager case 14).
    public static ByteWriter UpdateMemberGearScore(string playerName, int gearScore) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateMemberGearScore);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteInt(gearScore);

        return pWriter;
    }

    public static ByteWriter UpdateMember(PlayerInfo info) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateMember);
        pWriter.WriteUnicodeString(info.Name);
        GuildMember.WriteInfo(pWriter, info);

        return pWriter;
    }

    // s_guild_notify_change_name
    // - Guild name has changed to [{0}].
    public static ByteWriter UpdateName(string guildName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateName);
        pWriter.WriteUnicodeString(guildName);

        return pWriter;
    }

    // CGuildManager::OnPacketMemberAchievement: the line in the guild chat for a trophy grade a member earned.
    // Kind 2 is s_guild_notify_achieve_progress; any other kind is s_guild_notify_achieve_complete, and KMS2 sends 3
    // for the trophy's last grade.
    public static ByteWriter Achievement(string playerName, int trophyId, int grade, bool lastGrade) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Achievement);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteInt(trophyId);
        pWriter.WriteInt(grade);
        pWriter.WriteShort((short) (lastGrade ? 3 : 2));

        return pWriter;
    }

    // Guild 37 (OnPacketMemberRaidCount, guildmanager.cpp:2120): the member's name, the guild raid (its dungeon
    // room id) and the member's new value of it.
    public static ByteWriter MemberRaid(string playerName, int raidId, int value) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.MemberRaid);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteInt(raidId);
        pWriter.WriteInt(value);

        return pWriter;
    }

    public static ByteWriter CheckInTime(string playerName, long time) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.CheckInTime);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteLong(time);

        return pWriter;
    }

    public static ByteWriter ReceiveApplication(GuildApplication application) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.ReceiveApplication);
        pWriter.WriteClass<GuildApplication>(application);

        return pWriter;
    }

    public static ByteWriter WithdrawApplication(long applicationId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.WithdrawApplication);
        pWriter.WriteLong(applicationId);

        return pWriter;
    }

    // s_guild_notify_search_join_accept
    // - {0} accepted {1}'s guild membership application.
    // s_guild_notify_search_join_reject
    // - {0} denied {1}'s guild membership application.
    public static ByteWriter NotifyApplication(string requestorName, string playerName, long applicationId, bool accepted) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyApplication);
        pWriter.WriteUnicodeString(requestorName);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteBool(accepted);
        pWriter.WriteLong(applicationId);

        return pWriter;
    }

    public static ByteWriter NotifyApplicant(string guildName, long applicationId, bool accepted) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyApplicant);
        pWriter.WriteUnicodeString(guildName);
        pWriter.WriteLong(applicationId);
        pWriter.WriteBool(accepted);

        return pWriter;
    }

    public static ByteWriter GuildExperience(int exp) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.GuildExperience);
        pWriter.WriteInt(exp);

        return pWriter;
    }

    public static ByteWriter GuildFunds(int funds) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.GuildFunds);
        pWriter.WriteInt(funds);

        return pWriter;
    }

    // s_guild_gain_contribution
    // - You have acquired {0} contributions.
    // Guild 61: the client sets every member's weekly contribution to 0, then the dispatcher's case 0x3d runs on into
    // 0x3e (clears the members' guild raid records, so this one packet is also the raid reset) and into 0x76
    // (OnPacketVsGameTicket: bool registered, bool notify; guildmanager.cpp:367-369, exe 0x1413b68b4). Without the two
    // bools the client throws on the read. The channel does not hold the vs game ticket, so it sends "not registered"
    // without a line; the World re-sends the ticket to the guilds that hold one after the reset.
    public static ByteWriter WeeklyContributionReset() {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.WeeklyContributionReset);
        pWriter.WriteBool(false); // registered
        pWriter.WriteBool(false); // notify

        return pWriter;
    }

    public static ByteWriter GuildContribution(GuildMember member, int contribution) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.GuildContribution);
        pWriter.WriteUnicodeString(member.Name);
        pWriter.WriteInt(contribution);
        pWriter.WriteInt(member.WeeklyContribution);
        pWriter.WriteInt(member.TotalContribution);

        return pWriter;
    }

    // s_guild_notify_use_skill_user
    // - {0} used the {1} skill.
    public static ByteWriter NotifyUseBuff(string playerName, GuildBuff buff) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyUseBuff);
        pWriter.WriteUnicodeString(playerName);
        pWriter.Write<GuildBuff>(buff);

        return pWriter;
    }

    // s_guild_notify_upgrade_skill
    // - {0} upgraded the skill [{1}].
    public static ByteWriter UpgradeBuff(string playerName, GuildBuff buff) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpgradeBuff);
        pWriter.WriteUnicodeString(playerName);
        pWriter.Write<GuildBuff>(buff);

        return pWriter;
    }

    // s_guild_notify_house_upgrade
    // - {0} has changed the guild house theme to [{2}].
    public static ByteWriter UpgradeHouse(string playerName, int rank, int theme) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpgradeHouse);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteInt(rank);
        pWriter.WriteInt(theme);

        return pWriter;
    }

    // s_guild_notify_change_poster
    // - {0} has changed the guild poster.
    public static ByteWriter UpdatePoster(GuildPoster poster) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdatePoster);
        pWriter.WriteLong(poster.OwnerId);
        pWriter.WriteUnicodeString(poster.OwnerName);
        pWriter.WriteInt(poster.Id);
        pWriter.WriteUnicodeString(poster.Picture);

        return pWriter;
    }

    // s_guild_notify_upgrade_npc
    // - {0} upgraded {1} to level {2}.
    public static ByteWriter UpgradeNpc(string playerName, GuildNpc npc) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpgradeNpc);
        pWriter.WriteUnicodeString(playerName);
        pWriter.Write<GuildNpc>(npc);

        return pWriter;
    }

    public static ByteWriter UpdateLeader(string leaderName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateLeader);
        pWriter.WriteUnicodeString(leaderName);

        return pWriter;
    }

    // s_guild_change_notify
    // - The guild notice has been changed.
    public static ByteWriter UpdateNotice(string notice) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateNotice);
        pWriter.WriteBool(true); // success
        pWriter.WriteUnicodeString(notice);

        return pWriter;
    }

    public static ByteWriter UpdateEmblem(string emblem) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateEmblem);
        pWriter.WriteUnicodeString(emblem);

        return pWriter;
    }

    // s_guild_extend_capacity_success
    // - The maximum amount of guild members is now {0}.
    public static ByteWriter UpdateCapacity(int capacity) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateCapacity);
        pWriter.WriteInt(capacity);

        return pWriter;
    }

    // s_guild_change_grade_sucess
    // - The name and/or privileges of the {0} rank have changed.
    public static ByteWriter UpdateRank(GuildRank rank) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateRank);
        pWriter.WriteByte(rank.Id);
        pWriter.WriteClass<GuildRank>(rank);

        return pWriter;
    }

    // CGuildManager::FUN_1413bf2a0: the listed byte, then the focus.
    public static ByteWriter UpdateFocus(bool listed, GuildFocus focus) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UpdateFocus);
        pWriter.WriteBool(listed);
        pWriter.WriteInt((int) focus);

        return pWriter;
    }

    // s_mail_send
    // - Mail has been sent.
    public static ByteWriter SendMail() {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.SendMail);

        return pWriter;
    }

    public static ByteWriter AddTag(string playerName, string guildName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.AddTag);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteUnicodeString(guildName);

        return pWriter;
    }

    public static ByteWriter RemoveTag(string playerName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.RemoveTag);
        pWriter.WriteUnicodeString(playerName);

        return pWriter;
    }

    public static ByteWriter Error(GuildError error, int arg = 0) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteByte(1);
        pWriter.Write<GuildError>(error);
        pWriter.WriteInt(arg);

        return pWriter;
    }

    // s_guild_search_request_join_guild
    // - You have applied to the guild {0}.
    // Client CGuildManager::FUN_1413c5360 (case 0x52): an id (0 shows nothing), then the guild name. KMS2 sends
    // the id of the new request there, the one its row in Guild 86 starts with.
    public static ByteWriter SendApplication(long applicationId, string guildName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.SendApplication);
        pWriter.WriteLong(applicationId);
        pWriter.WriteUnicodeString(guildName);

        return pWriter;
    }

    // Client CGuildManager::FUN_1413c5440 (case 0x53): the guild id (0 shows nothing), then the guild name.
    public static ByteWriter CancelApplication(long guildId, string guildName) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.CancelApplication);
        pWriter.WriteLong(guildId);
        pWriter.WriteUnicodeString(guildName);

        return pWriter;
    }

    // CGuildManager::OnPacketJoinRequestAnswer, to the member who answered: the applicant's character id
    // (0 shows nothing), the applicant's name for the chat line, accepted or not.
    public static ByteWriter RespondApplication(long characterId, string applicantName, bool accepted) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.RespondApplication);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(applicantName);
        pWriter.WriteBool(accepted);

        return pWriter;
    }

    public static ByteWriter ListApplications(ICollection<GuildApplication> applications) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.ListApplications);
        pWriter.WriteInt(applications.Count);
        foreach (GuildApplication application in applications) {
            pWriter.WriteBool(true);
            pWriter.WriteClass<GuildApplication>(application);
        }

        return pWriter;
    }

    public static ByteWriter ListAppliedGuilds(ICollection<(GuildApplication Application, int MemberCount)> applications) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.ListAppliedGuilds);
        pWriter.WriteInt(applications.Count);
        foreach ((GuildApplication application, int memberCount) in applications) {
            // Client: CGuildJoinRequestForChar::ReadStream (guilddef_1.cpp:404). The int after the name is the
            // guild's experience (the join page turns it into the guild level); the text is the leader's name.
            pWriter.WriteBool(true);
            pWriter.WriteLong(application.Id);
            pWriter.WriteLong(application.Guild.Id);
            pWriter.WriteUnicodeString(application.Guild.Name);
            pWriter.WriteInt(application.Guild.Experience);
            pWriter.WriteUnicodeString(application.Guild.Emblem);
            pWriter.Write<AchievementInfo>(application.Guild.AchievementInfo);
            pWriter.WriteInt(memberCount);
            pWriter.WriteInt(application.Guild.Capacity);
            pWriter.WriteUnicodeString(application.Guild.LeaderName);
            pWriter.WriteLong(application.Applicant.AccountId);
            pWriter.WriteLong(application.Applicant.CharacterId);
            pWriter.WriteLong(application.CreationTime);
        }

        return pWriter;
    }

    // CGuildManager::OnPacketSearchResults, then CSearchGuildJoin::ReadStream per entry: id, name, emblem,
    // the trophy triple, member count, capacity, experience (the list shows the level it gives), the leader's
    // account and character ids and name. A count of 0 makes the client show its "no guild found" box.
    public static ByteWriter ListGuilds(ICollection<(GuildSearchEntry Entry, int Capacity)> guilds) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.ListGuilds);
        pWriter.WriteInt(guilds.Count);
        foreach ((GuildSearchEntry guild, int capacity) in guilds) {
            pWriter.WriteBool(true);
            pWriter.WriteLong(guild.Id);
            pWriter.WriteUnicodeString(guild.Name);
            pWriter.WriteUnicodeString(guild.Emblem);
            pWriter.WriteInt(); // trophy: combat
            pWriter.WriteInt(); // trophy: adventure
            pWriter.WriteInt(); // trophy: lifestyle
            pWriter.WriteInt(guild.MemberCount);
            pWriter.WriteInt(capacity);
            pWriter.WriteInt(guild.Experience);
            pWriter.WriteLong(guild.LeaderAccountId);
            pWriter.WriteLong(guild.LeaderCharacterId);
            pWriter.WriteUnicodeString(guild.LeaderName);
        }

        return pWriter;
    }

    // CGuildManager::FUN_1413c3780 (guildmanager.cpp:2533): the member's name, then the event
    // (CGuildCustomEvent: id, time of opening). s_guild_notify_open_event_user
    public static ByteWriter NotifyOpenEvent(string playerName, GuildEvent opened) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.NotifyOpenEvent);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteInt(opened.Id);
        pWriter.WriteInt(opened.Value);

        return pWriter;
    }

    // CGuildManager::OnPacketOpenEvent (guildmanager.cpp:1911): the event id. s_guild_open_event
    public static ByteWriter OpenEvent(int eventId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.OpenEvent);
        pWriter.WriteInt(eventId);

        return pWriter;
    }

    public static ByteWriter UseBuff(int buffId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UseBuff);
        pWriter.WriteInt(buffId);

        return pWriter;
    }

    public static ByteWriter UsePersonalBuff(int buffId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.UsePersonalBuff);
        pWriter.WriteInt(buffId);

        return pWriter;
    }

    // CGuildManager::OnPacketPvpHistory (guildmanager.cpp:2805): a count, then per row a byte and, when it
    // is 1, the history row (CGuildPVPHistoryData). The client adds the rows to the ones it holds (the newest 10).
    public static ByteWriter PvpHistory(IReadOnlyCollection<GuildPvpMatch> matches) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpHistory);
        pWriter.WriteInt(matches.Count);
        foreach (GuildPvpMatch match in matches) {
            pWriter.WriteByte(1);
            match.WriteTo(pWriter);
        }

        return pWriter;
    }

    // CGuildManager::OnPacketPvpSeasonHistory (guildmanager.cpp:2826): the same shape with season rows. The
    // client empties its season list first, then redraws the tab.
    public static ByteWriter PvpSeason(IReadOnlyCollection<GuildPvpSeason> seasons) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpSeason);
        pWriter.WriteInt(seasons.Count);
        foreach (GuildPvpSeason season in seasons) {
            pWriter.WriteByte(1);
            season.WriteTo(pWriter);
        }

        return pWriter;
    }

    // 0x27 (FUN_1413c2860): the guild's championship state (CGuildInfo::SetPvpState).
    public static ByteWriter PvpState(GuildPvpState state) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpState);
        pWriter.WriteByte((byte) state);

        return pWriter;
    }

    // 0x28: the guild was matched (0x3bf, then the join prompt) or not (0x3c0).
    public static ByteWriter PvpMatched(bool matched) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpMatched);
        pWriter.WriteBool(matched);

        return pWriter;
    }

    // 0x26 (CGuildManager::FUN_1413c23d0, guildmanager.cpp:2329): per member, the name and the championship matches
    // fought this season and in all (CGuildMember fe0, fe4); an open guild dialog redraws each named member's row.
    public static ByteWriter PvpMemberCounts(ICollection<GuildMember> members) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpMemberCounts);
        pWriter.WriteInt(members.Count);
        foreach (GuildMember member in members) {
            pWriter.WriteUnicodeString(member.Name);
            pWriter.WriteInt(member.PvpSeasonCount);
            pWriter.WriteInt(member.PvpTotalCount);
        }

        return pWriter;
    }

    // 0x29: the guild's new score, wins and losses (0x3b8 when the grade changed).
    public static ByteWriter PvpRating(int score, int wins, int losses) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpRating);
        pWriter.WriteInt(score);
        pWriter.WriteInt(wins);
        pWriter.WriteInt(losses);

        return pWriter;
    }

    // 0x2a (OnPacketBattleResult): won (0x3bb) or lost (0x3bc) against the named guild.
    public static ByteWriter PvpResult(bool won, string opponent) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpResult);
        pWriter.WriteBool(won);
        pWriter.WriteUnicodeString(opponent);

        return pWriter;
    }

    // 0x2b (OnPacketBattleApplied): the member who registered the guild (s_guild_notify_pvp_regist).
    public static ByteWriter PvpRegistered(string name) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpRegistered);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // 0x2c (OnPacketGuildBattleNotice): the member who took the registration back (s_guild_notify_pvp_unregist).
    public static ByteWriter PvpUnregistered(string name) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.WriteByte((byte) Command.PvpUnregistered);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // CGuildManager::OnPacketStorageGiftResult (0x6d, guildmanager.cpp:2963): a result byte; the receiver's
    // name follows only for the result 0. 54 prints the need-period text, any other result prints nothing.
    public static ByteWriter StorageGift(GuildError result, string receiver = "") {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.StorageGift);
        pWriter.WriteByte((byte) result);
        if (result == GuildError.none) {
            pWriter.WriteUnicodeString(receiver);
        }

        return pWriter;
    }

    // CGuildManager::OnPacketVsGameTicket (0x76, guildmanager.cpp:2656): whether the guild holds a vs-game
    // ticket, and whether the client prints the registered / unregistered line.
    public static ByteWriter VsGameTicket(bool registered, bool notify) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.VsGameTicket);
        pWriter.WriteBool(registered);
        pWriter.WriteBool(notify);

        return pWriter;
    }

    // CGuildManager::FUN_1413c45e0 (0x77, guildmanager.cpp:2679): the id of the vs-game that opened; the
    // client clears its ticket flag and, for an id other than 0, asks whether to enter (30 seconds).
    public static ByteWriter VsGameOpen(int gameId) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.VsGameOpen);
        pWriter.WriteInt(gameId);

        return pWriter;
    }

    // CGuildManager case 0x75 (guildmanager.cpp:369): no opponent was found for the guild's vs-game; the client clears
    // its ticket and prints s_guild_notify_vsGame_match_canceled.
    public static ByteWriter VsGameMatchCanceled() {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.VsGameMatchCanceled);

        return pWriter;
    }

    // CGuildManager::FUN_1413c4870 (0x7a) -> UIPvPGuildTeamResultDialog::SetVsGameResult: both guilds (id, name,
    // mark, score); the client takes win/lose/draw from the scores and names the other guild in its line.
    public static ByteWriter VsGameResult(IReadOnlyList<(long GuildId, string Name, string Mark, int Score)> guilds) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.VsGameResult);
        for (int i = 0; i < 2; i++) {
            (long guildId, string name, string mark, int score) = i < guilds.Count ? guilds[i] : (0L, string.Empty, string.Empty, 0);
            pWriter.WriteLong(guildId);
            pWriter.WriteUnicodeString(name);
            pWriter.WriteUnicodeString(mark);
            pWriter.WriteInt(score);
        }

        return pWriter;
    }

    // CGuildManager::OnPacketVsGameReward (0x7b): the notice of a vs-game reward, kind 1 the guild exp and 2 the
    // guild fund.
    public static ByteWriter VsGameReward(byte kind, int amount) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.VsGameReward);
        pWriter.WriteByte(kind);
        pWriter.WriteInt(amount);

        return pWriter;
    }

    // CGuildManager::FUN_1413c3e50 (0x6e): one storage entry; the client sets the amount of its id and rarity
    // (SetStorageItem; 0 removes it), redraws the storage page and asks for the log (112) again when it is opened.
    public static ByteWriter StorageItem(RewardItem item) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.StorageItem);
        Guild.WriteStorageItem(pWriter, item);

        return pWriter;
    }

    // CGuildManager::OnPacketStorageHistory (0x70, guildmanager.cpp:2980): a count, then per row the giver and
    // the receiver (Unk_GFRecord: account id, character id, name), the item and the time.
    public static ByteWriter StorageHistory(IReadOnlyCollection<GuildStorageLog> log) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.StorageHistory);
        pWriter.WriteInt(log.Count);
        foreach (GuildStorageLog entry in log) {
            pWriter.WriteLong(entry.GiverAccountId);
            pWriter.WriteLong(entry.GiverCharacterId);
            pWriter.WriteUnicodeString(entry.GiverName);
            pWriter.WriteLong(entry.ReceiverAccountId);
            pWriter.WriteLong(entry.ReceiverCharacterId);
            pWriter.WriteUnicodeString(entry.ReceiverName);
            Guild.WriteStorageItem(pWriter, new RewardItem(entry.ItemId, entry.Rarity, entry.Amount));
            pWriter.WriteLong(entry.Time);
        }

        return pWriter;
    }

    public static ByteWriter AddContribution(int addExp, int addFunds) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.AddContribution);
        pWriter.WriteInt(addExp);
        pWriter.WriteInt(addFunds);

        return pWriter;
    }

    public static ByteWriter Donated(GuildMember member) {
        var pWriter = Packet.Of(SendOp.Guild);
        pWriter.Write<Command>(Command.Donated);
        pWriter.WriteInt(member.DailyDonationCount);
        pWriter.WriteLong(member.DonationTime);

        return pWriter;
    }
}

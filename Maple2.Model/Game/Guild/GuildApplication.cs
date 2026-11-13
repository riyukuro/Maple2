using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

public class GuildApplication : IByteSerializable {
    public required long Id { get; init; }
    public required Guild Guild;
    public required PlayerInfo Applicant;
    public long CreationTime;

    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(Id);
        writer.WriteLong(Guild.Id);
        // CGuildJoinRequestForGuild::ReadStream: the accept page names the fourth id "charSn" and uses it for
        // the profile picture, so it is the character id. Guild 84 carries the first id ("requestSn").
        writer.WriteLong(Applicant.AccountId);
        writer.WriteLong(Applicant.CharacterId);
        writer.WriteUnicodeString(Applicant.Name);
        writer.WriteUnicodeString(Applicant.Picture);
        // The job code, then the job: SetFromJoinRequest (guilddef_1.cpp:696) copies the first into the member's job
        // code and the second into its job, and the request list names the second from string table 6
        // (uiguilddialog.cpp:2034), the order of GuildMember.WriteInfo.
        writer.WriteInt((int) Applicant.Job.Code());
        writer.WriteInt((int) Applicant.Job);
        writer.WriteInt(Applicant.Level);
        writer.Write<AchievementInfo>(Applicant.AchievementInfo);
        writer.WriteLong(CreationTime);
    }
}

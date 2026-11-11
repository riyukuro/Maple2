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
        // CGuildJoinRequestForGuild::ReadStream: the fourth id is the one the accept page hands back as
        // "charSn" (Guild 84) and uses for the profile picture, so it is the character id.
        writer.WriteLong(Applicant.AccountId);
        writer.WriteLong(Applicant.CharacterId);
        writer.WriteUnicodeString(Applicant.Name);
        writer.WriteUnicodeString(Applicant.Picture);
        writer.Write<Job>(Applicant.Job);
        writer.WriteInt((int) Applicant.Job.Code());
        writer.WriteInt(Applicant.Level);
        writer.Write<AchievementInfo>(Applicant.AchievementInfo);
        writer.WriteLong(CreationTime);
    }
}

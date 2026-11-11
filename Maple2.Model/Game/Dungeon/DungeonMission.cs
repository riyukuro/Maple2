using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Dungeon;

public class DungeonMission : IByteSerializable {
    public readonly DungeonMissionMetadata Metadata;
    public int Id => Metadata.Id;
    public short Score { get; private set; }
    public short Counter { get; private set; }

    /// <summary>One of the room's userMissions (scored per player); else a party mission.</summary>
    public readonly bool IsUserMission;

    public DungeonMission(DungeonMissionMetadata metadata, bool isUserMission = false) {
        Metadata = metadata;
        IsUserMission = isUserMission;
    }

    public bool Update(int counter = 1) {
        if (Counter >= Metadata.ApplyCount) {
            return false;
        }

        Counter += (short) counter;
        float percentage = (float) Counter / Metadata.ApplyCount;
        Score = (short) (percentage * Metadata.MaxScore);
        return true;
    }

    public void Complete() {
        Counter = Metadata.ApplyCount;
        Score = Metadata.MaxScore;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteShort(Score);
        // The client picks the reader by the mission's type: every type reads the score, the play time
        // type then reads two longs, the user count type nothing more, all others the counter.
        switch (Metadata.Type) {
            case DungeonMissionType.PlayTime:
                writer.WriteLong();
                writer.WriteLong();
                break;
            case DungeonMissionType.LimitUserCount:
                break;
            default:
                writer.WriteShort(Counter);
                break;
        }
    }
}

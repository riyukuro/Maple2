using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Dungeon;

// A season rank reward of a room (its seasonRankRewardID): the client's DungeonRankReward (m_18 id, m_1c count, m_20
// expiry). The panel marks rank i given while i <= RankClaimed and the server time is before ExpiryTimestamp
// (DungeonRecordManagerCli::GetRankRewardCount, dungeonrecordcli.cpp:608): the end of the season week.
public class DungeonRankReward : IByteSerializable {
    public readonly int Id;
    public int RankClaimed { get; set; }
    public long ExpiryTimestamp { get; set; }

    public DungeonRankReward(int id) {
        Id = id;
    }
    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteInt(RankClaimed);
        writer.WriteLong(ExpiryTimestamp);
    }
}

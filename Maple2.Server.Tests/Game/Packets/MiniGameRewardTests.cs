using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class MiniGameRewardTests {
    // Client: UIDungeonRewardDialog::OnRoundPacket (client/ms2/uidungeonrewarddialog.cpp:393) reads 2 ints, then
    // DungeonRewardInfo::Read (commonfiles/commonstruct_wa.cpp:337): int count x (byte, int), int count x
    // (int item id, int count, int rank, 3 bools).
    [Test]
    public void MiniGameRewardItemIsIdCountRank() {
        var record = new MiniGameUserRecord(1) { MinRound = 1, TotalRounds = 5, ShowResult = true, ClearedRounds = 3 };
        record.RewardItems.Add(new RewardItem(20000027, 4, 7));
        int rewardKinds = System.Enum.GetValues<DungeonRewardType>().Length;

        ByteWriter writer = DungeonRewardPacket.MiniGame(record);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DungeonReward));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(rewardKinds));
        for (int i = 0; i < rewardKinds; i++) {
            reader.ReadByte();
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000027));
        Assert.That(reader.ReadInt(), Is.EqualTo(7)); // count
        Assert.That(reader.ReadInt(), Is.EqualTo(4)); // rank
        reader.ReadBool();
        reader.ReadBool();
        reader.ReadBool();
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 4 + 4 + rewardKinds * 5 + 4 + 15));
    }
}

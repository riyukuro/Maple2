using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Packets;

// The dungeon result (DungeonReward mode 0). Client: UIDungeonRewardDialog::OnRewardPacket
// (client/ms2/uidungeonrewarddialog.cpp:557): bool win, int room, bool party, int seconds, int best score, int run
// score, int bonus bits, then the main and the bonus DungeonRewardInfo (commonfiles/commonstruct_wa.cpp:337).
public class DungeonResultTests {
    private const int Room = 90000200;
    private const string Head = "1B00" + "00" + "01" + "484B5D05" + "00" + "7B000000";
    private const string NoItems = "00000000";

    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));

    private static string Hex(DungeonUserRecord record) => Convert.ToHexString(DungeonRewardPacket.Dungeon(record).ToArray());

    private static DungeonUserRecord Cleared(DungeonBonusFlag flag) {
        var record = new DungeonUserRecord(Room, 1) { IsDungeonSuccess = true, TotalSeconds = 123 };
        record.Flag |= flag;
        record.Rewards[DungeonRewardType.Meso] = 500;
        record.Rewards[DungeonRewardType.Exp] = 1000;
        record.Rewards[DungeonRewardType.Prestige] = 75000;
        return record;
    }

    private const string Main = "03000000" + "01" + "F4010000" + "02" + "E8030000" + "03" + "F8240100";

    [Test]
    public void OpcodeIsDungeonReward() {
        Assert.That((ushort) SendOp.DungeonReward, Is.EqualTo(0x1B));
    }

    // A counted clear: bits 0x2, the three main amounts in key order, an empty bonus info (no bonus stage).
    [Test]
    public void CountedClear_WritesBit1AndAnEmptyBonus() {
        Assert.That(Hex(Cleared(DungeonBonusFlag.Clear)), Is.EqualTo(Head + "00000000" + "FFFFFFFF" + "02000000"
            + Main + NoItems + "00000000" + NoItems));
    }

    // Bit 0 (the record's initial value) is never sent: a clear without a count left has no bonus line.
    [Test]
    public void UncountedClear_WritesNoBit() {
        var record = new DungeonUserRecord(Room, 1) { IsDungeonSuccess = true, TotalSeconds = 123 };
        Assert.That(Hex(record), Is.EqualTo(Head + "00000000" + "FFFFFFFF" + "00000000"
            + "03000000" + "01" + "00000000" + "02" + "00000000" + "03" + "00000000" + NoItems + "00000000" + NoItems));
    }

    [TestCase(DungeonBonusFlag.Clear | DungeonBonusFlag.Event, "06000000")]
    [TestCase(DungeonBonusFlag.Clear | DungeonBonusFlag.Helper, "12000000")]
    [TestCase(DungeonBonusFlag.Helper, "10000000")]
    [TestCase(DungeonBonusFlag.Clear | DungeonBonusFlag.Event | DungeonBonusFlag.Helper | DungeonBonusFlag.UnitedWeekly, "16020000")]
    public void BonusBits_AreTheClientsLines(DungeonBonusFlag flag, string bits) {
        Assert.That(Hex(Cleared(flag)), Is.EqualTo(Head + "00000000" + "FFFFFFFF" + bits + Main + NoItems + "00000000" + NoItems));
    }

    // The weekly step reward: bits 0x202 and a bonus info with only the amounts given, then its item.
    [Test]
    public void WeeklyReward_WritesBit9AndOnlyTheGivenBonusAmounts() {
        DungeonUserRecord record = Cleared(DungeonBonusFlag.Clear | DungeonBonusFlag.UnitedWeekly);
        record.BonusRewards[DungeonRewardType.Exp] = 2000;
        record.BonusRewardItems.Add(new RewardItem(20000027, 4, 2));

        Assert.That(Hex(record), Is.EqualTo(Head + "00000000" + "FFFFFFFF" + "02020000" + Main + NoItems
            + "01000000" + "02" + "D0070000"
            + "01000000" + "1B2D3101" + "02000000" + "04000000" + "000000"));
    }

    // A rank room: the best score before the clear, then the run's.
    [Test]
    public void RankScores_AreBestThenRun() {
        DungeonUserRecord record = Cleared(DungeonBonusFlag.Clear);
        record.HighestScore = 2000;
        record.Score = 1000;
        Assert.That(Hex(record), Is.EqualTo(Head + "D0070000" + "E8030000" + "02000000" + Main + NoItems + "00000000" + NoItems));
    }

    private static GameSession StartInRoom(TestGameSession test, int rewardCount) {
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000201, rewardCount);
        room = room with { Reward = room.Reward with { Exp = 1000, Meso = 500 } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> { [ExpType.dungeonClear] = 75000 }));
        GameSession session = test.Start();
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
        return session;
    }

    // A counted clear names the table's prestige amount, gives it, sends the exp with the dungeon message code
    // and has the clear bit only (no weekly line without a weekly reward).
    [Test]
    public void CountedClear_GivesPrestigeExpAndTheDungeonExpMessage() {
        var test = new TestGameSession();
        GameSession session = StartInRoom(test, 10);
        long prestigeBefore = session.Exp.PrestigeCurrentExp;

        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        DungeonUserRecord record = session.Dungeon.UserRecord!;
        Assert.That(record.Flag & ~DungeonBonusFlag.None, Is.EqualTo(DungeonBonusFlag.Clear));
        Assert.That(record.Rewards, Is.EqualTo(new Dictionary<DungeonRewardType, int> {
            [DungeonRewardType.Meso] = 500, [DungeonRewardType.Exp] = 1000, [DungeonRewardType.Prestige] = 75000,
        }));
        Assert.That(session.Exp.PrestigeCurrentExp - prestigeBefore, Is.EqualTo(75000));
        List<byte[]> sent = test.TakeSent();
        byte[] expUp = sent.Single(packet => TestGameSession.Op(packet) == SendOp.ExpUp);
        Assert.That(Convert.ToHexString(expUp, 3, 10), Is.EqualTo("E803000000000000" + "3D04"));
        byte[] result = sent.Single(packet => TestGameSession.Op(packet) == SendOp.DungeonReward);
        Assert.That(Convert.ToHexString(result), Is.EqualTo(Hex(record)));
        Assert.That(Convert.ToHexString(result, 21, 4), Is.EqualTo("02000000"));
        // KMS2's order: the two record updates and the reward notice, the result, then the meso and the exp it names,
        // both with the dungeon message code, and the prestige exp.
        Assert.That(sent.Select(TestGameSession.Op).SkipWhile(op => op != SendOp.RoomDungeon).Where(op =>
                op is SendOp.RoomDungeon or SendOp.DungeonReward or SendOp.Meso or SendOp.ExpUp or SendOp.Prestige),
            Is.EqualTo(new[] { SendOp.RoomDungeon, SendOp.RoomDungeon, SendOp.RoomDungeon, SendOp.DungeonReward, SendOp.Meso, SendOp.ExpUp, SendOp.Prestige }));
        byte[] meso = sent.Single(packet => TestGameSession.Op(packet) == SendOp.Meso);
        Assert.That(Convert.ToHexString(meso, 2, 12), Is.EqualTo(Long(session.Currency.Meso) + "3D040000"));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void ClearWithoutACountLeft_GivesNothingAndNoBit() {
        var test = new TestGameSession();
        GameSession session = StartInRoom(test, 0);
        long prestigeBefore = session.Exp.PrestigeCurrentExp;

        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Any(packet => TestGameSession.Op(packet) == SendOp.ExpUp), Is.False);
        Assert.That(session.Exp.PrestigeCurrentExp, Is.EqualTo(prestigeBefore));
        Assert.That(TestGameSession.Op(sent[^1]), Is.EqualTo(SendOp.DungeonReward));
        Assert.That(Convert.ToHexString(sent[^1], 21, 4), Is.EqualTo("00000000"));
        Assert.That(sent.Any(packet => TestGameSession.Op(packet) == SendOp.Meso), Is.False);
        Assert.That(session.Dungeon.UserRecord!.Rewards[DungeonRewardType.Prestige], Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // A clear that reaches a weekly step: the step's exp and meso come after the result and the clear's own, with
    // the weekly message code (1146), and the result names them in its bonus list with bit 9.
    [Test]
    public void WeeklyStep_IsGivenAfterTheResultWithTheWeeklyCode() {
        const int union = 1006, rewardId = 5001;
        var test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000201);
        room = room with { Reward = room.Reward with { Exp = 1000, Meso = 500, UnionRewardId = union } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [Room] = room, [union] = TestGameSession.DungeonRoom(union, 90000201),
        }));
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int> { [1] = rewardId }, new Dictionary<int, DungeonMissionRankMetadata>()));
        test.SetTable(new RewardContentTable(
            new Dictionary<int, RewardContentTable.Base> { [rewardId] = new(rewardId, 100001, 100002, 0f, 0f, 0, 0) },
            new Dictionary<int, RewardContentTable.Item>(), new Dictionary<int, long> { [100001] = 700 },
            new Dictionary<int, Dictionary<int, long>>(), new Dictionary<int, long> { [100002] = 900 }));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> { [ExpType.dungeonClear] = 75000 }));
        GameSession session = test.Start();
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
        long mesoBefore = session.Currency.Meso;

        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        DungeonUserRecord record = session.Dungeon.UserRecord!;
        Assert.That(record.Flag & ~DungeonBonusFlag.None, Is.EqualTo(DungeonBonusFlag.Clear | DungeonBonusFlag.UnitedWeekly));
        Assert.That(record.BonusRewards[DungeonRewardType.Meso], Is.EqualTo(700));
        Assert.That(record.BonusRewards[DungeonRewardType.Exp], Is.EqualTo(900));
        List<byte[]> sent = test.TakeSent();
        int resultAt = sent.FindIndex(packet => TestGameSession.Op(packet) == SendOp.DungeonReward);
        Assert.That(Convert.ToHexString(sent[resultAt], 21, 4), Is.EqualTo("02020000"));
        List<byte[]> after = sent.Skip(resultAt + 1).Where(packet => TestGameSession.Op(packet) is SendOp.Meso or SendOp.ExpUp).ToList();
        Assert.That(after.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Meso, SendOp.ExpUp, SendOp.Meso, SendOp.ExpUp }));
        Assert.That(sent.Take(resultAt).Select(TestGameSession.Op), Has.None.EqualTo(SendOp.Meso).And.None.EqualTo(SendOp.ExpUp));
        Assert.That(Convert.ToHexString(after[0], 2, 12), Is.EqualTo(Long(mesoBefore + 500) + "3D040000"));
        Assert.That(Convert.ToHexString(after[2], 2, 12), Is.EqualTo(Long(mesoBefore + 1200) + "7A040000"));
        Assert.That(Convert.ToHexString(after[3], 3, 10), Is.EqualTo("8403000000000000" + "7A04"));
        Assert.That(test.Errors, Is.Empty);
    }
}

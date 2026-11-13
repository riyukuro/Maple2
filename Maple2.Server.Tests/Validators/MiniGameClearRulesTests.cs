using System;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Validators;

public class MiniGameClearRulesTests {
    [TestCase("UserOpenMiniGameExtraReward", true)]
    [TestCase("miniGame", false)]
    [TestCase("", false)]
    public void Reward_CountsForTheUserOpenContent(string contentType, bool expected) {
        Assert.That(MiniGameClearRules.CountsAsUserOpenClear(contentType), Is.EqualTo(expected));
    }

    // The row lists the three mini-game fields; a reward on another field does not count.
    [TestCase(81000001, true)]
    [TestCase(81000003, true)]
    [TestCase(81000004, false)]
    public void Row_CountsItsFields(int mapId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.useropen_minigame_clear, 1,
            new ConditionMetadata.Parameters(Integers: [81000001, 81000002, 81000003]), null);
        Assert.That(condition.Check(null!, codeLong: mapId), Is.EqualTo(expected));
    }

    [TestCase("UserMassive_Crazyrunner", true)]
    [TestCase("UserMassive_Springbeach", true)]
    [TestCase("UserMassive_Escape", true)]
    [TestCase("miniGame", false)]
    public void Game_IsUserOpen(string gameName, bool expected) {
        Assert.That(MiniGameClearRules.IsUserOpenGame(gameName), Is.EqualTo(expected));
    }

    // userMassiveExtraRewardMax is 5: the fifth clear of the day pays, the sixth does not; a missing constant pays none.
    [TestCase(0, 5, true)]
    [TestCase(4, 5, true)]
    [TestCase(5, 5, false)]
    [TestCase(9, 5, false)]
    [TestCase(0, 0, false)]
    public void ExtraReward_CappedPerDay(int paidToday, int max, bool expected) {
        Assert.That(MiniGameClearRules.CanPayExtraReward(paidToday, max), Is.EqualTo(expected));
    }

    // The client prints a and b - a as {0} (the daily count) and {1} (the ones left), and drops a > b.
    [TestCase(0, 5, 5, 10)]
    [TestCase(1, 5, 5, 9)]
    [TestCase(5, 5, 5, 5)]
    [TestCase(7, 5, 5, 5)]
    public void Notice_ArgsGiveTheCountAndTheLeft(int paidToday, int max, int a, int b) {
        (int A, int B) args = MiniGameClearRules.NoticeArgs(paidToday, max);
        Assert.That(args, Is.EqualTo((a, b)));
        Assert.That(args.A, Is.LessThanOrEqualTo(args.B));
        Assert.That(args.B - args.A, Is.EqualTo(Math.Max(0, max - paidToday)));
    }

    // The count of an earlier day reads as 0, so the day's first clear pays again.
    [Test]
    public void ExtraReward_CountOfAnEarlierDayIsZero() {
        long lastReset = 1_000_000;
        Assert.That(PetFusionRules.DailyCount(5, lastReset - 1, lastReset), Is.EqualTo(0));
        Assert.That(PetFusionRules.DailyCount(5, lastReset + 1, lastReset), Is.EqualTo(5));
        Assert.That(PetFusionRules.DailyCount(5, 0, lastReset), Is.EqualTo(0));
    }

    [Test]
    public void Notice_PacketLayout() {
        byte[] bytes = PlayerHostPacket.MiniGameRewardReceive(5, 9).ToArray();
        Assert.That(bytes[2], Is.EqualTo(5));
        Assert.That(BitConverter.ToInt32(bytes, 3), Is.EqualTo(5));
        Assert.That(BitConverter.ToInt32(bytes, 7), Is.EqualTo(9));
        Assert.That(bytes.Length, Is.EqualTo(11));
        Assert.That(PlayerHostPacket.MiniGameRewardNotice(5, 10).ToArray()[2], Is.EqualTo(4));
    }

    // A user-opened game's exp, the losers' bonus included, is userMiniGame; every other game's is miniGame.
    [Test]
    public void ExpTypeOf_UserOpenGames() {
        Assert.That(MiniGameClearRules.ExpTypeOf("UserMassive_Escape"), Is.EqualTo(ExpType.userMiniGame));
        Assert.That(MiniGameClearRules.ExpTypeOf("UserMassive_Crazyrunner"), Is.EqualTo(ExpType.userMiniGame));
        Assert.That(MiniGameClearRules.ExpTypeOf("UserMassive_Springbeach"), Is.EqualTo(ExpType.userMiniGame));
        Assert.That(MiniGameClearRules.ExpTypeOf("trapmaster"), Is.EqualTo(ExpType.miniGame));
        Assert.That(MiniGameClearRules.ExpTypeOf(""), Is.EqualTo(ExpType.miniGame));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// SurvivalLevelExp (KR 20301747-20301791 Royale Experience Tonic, <v SurvivalExp="N"/>): the client registers no
// function for it, so UseItem (unit_140d1ae30 @0x140d1bd60) sends the plain use; the server adds the survival exp.
public class SurvivalLevelExpTests {
    private const int ItemId = 20301747;
    private const int RewardId = 20000995;
    private TestGameSession test = null!;
    private Item tonic = null!;

    // Level 2 at 2400, level 3 at 4320 more (the last); reaching level 2 gives its level reward.
    private static MapleSurvivalTable Table() {
        var reward = new MapleSurvivalTable.Reward("item", [RewardId], [1], [1]);
        return new MapleSurvivalTable(
            new Dictionary<int, MapleSurvivalTable.Period> { [1] = new(1, 1, new DateTime(2018, 8, 22), new DateTime(2099, 1, 1), 800) },
            new Dictionary<int, long> { [1] = 0, [2] = 2400, [3] = 4320 },
            new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> { [2] = [reward] },
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>());
    }

    // The level-reward mode (SurvivalContents03 off, as on KR Live) unless passMode.
    private GameSession Start(string parameters = "<v SurvivalExp=\"2500\" />", int level = 1, long exp = 0, bool passMode = false) {
        test = new TestGameSession();
        if (!passMode) {
            test.SetFeaturesOff(Maple2.Model.Game.Pvp.SurvivalRules.PassFeature);
        }
        test.SetTable(Table());
        test.Register(TestGameSession.Metadata(RewardId));
        tonic = test.Store(TestGameSession.Metadata(ItemId,
            function: new ItemMetadataFunction(ItemFunction.SurvivalLevelExp, "SurvivalLevelExp", parameters, false), type: 10), amount: 2);
        GameSession session = test.Start();
        session.Player.Value.Account.SurvivalLevel = level;
        session.Player.Value.Account.SurvivalExp = exp;
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) {
        var handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(tonic.Uid);
            writer.WriteUnicodeString(string.Empty);
        }));
    }

    private static int Amount(GameSession session, int itemId) {
        int amount = 0;
        foreach (Item item in session.Item.Inventory.Filter(item => item.Id == itemId)) {
            amount += item.Amount;
        }
        return amount;
    }

    // Before, the server had no SurvivalLevelExp case: the use did nothing and the tonic stayed.
    [Test]
    public void Use_AddsTheExpWithTheLevelReward() {
        GameSession session = Start();
        Use(session);
        Account account = session.Player.Value.Account;
        Assert.That((account.SurvivalLevel, account.SurvivalExp), Is.EqualTo((2, 100L)));
        Assert.That(session.Item.Inventory.Get(tonic.Uid)!.Amount, Is.EqualTo(1));
        Assert.That(Amount(session, RewardId), Is.EqualTo(1));
        // The record (Survival 0x17) with the exp gained.
        Assert.That(test.TakeSent(), Has.Some.EqualTo(SurvivalPacket.UpdateStats(account, 2500).ToArray()));
    }

    // A game's level-up gives the level reward and the result (0x11) lists it in its reward slots: the winner of 1 with
    // 5 kills gains 2500 (SurvivalRules.GameExp), level 2. In the pass mode none is given or listed.
    [Test]
    public void GameResult_ListsTheLevelReward() {
        GameSession session = Start();
        session.Survival = new Maple2.Server.Game.Manager.SurvivalManager(session);
        Maple2.Server.Game.Manager.MapleSurvivalProgress.Result(session, 1, 1, 5, 60, out var items);
        Assert.That(session.Player.Value.Account.SurvivalLevel, Is.EqualTo(2));
        Assert.That(items, Is.EqualTo(new[] { new Maple2.Server.Game.Manager.MapleSurvivalProgress.ResultItem(RewardId, 1, 1) }));
        Assert.That(Amount(session, RewardId), Is.EqualTo(1));
        // The lobby sent again ends with its level and exp (0x15), after the record (0x17).
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent[^1], Is.EqualTo(MapleSurvivalPacket.LevelProgress(100, 2, 2).ToArray()));

        session = Start(passMode: true);
        session.Survival = new Maple2.Server.Game.Manager.SurvivalManager(session);
        Maple2.Server.Game.Manager.MapleSurvivalProgress.Result(session, 1, 1, 5, 60, out items);
        Assert.That((session.Player.Value.Account.SurvivalLevel, items.Count, Amount(session, RewardId)), Is.EqualTo((2, 0, 0)));
    }

    // In the pass mode (SurvivalContents03 on) the level reached gives no level reward: its pass rewards are claimed.
    [Test]
    public void Use_PassMode_GivesNoLevelReward() {
        GameSession session = Start(passMode: true);
        Use(session);
        Account account = session.Player.Value.Account;
        Assert.That((account.SurvivalLevel, account.SurvivalExp), Is.EqualTo((2, 100L)));
        Assert.That(Amount(session, RewardId), Is.EqualTo(0));
    }

    // An account of another period starts the open one again; the restored client is told first (0x18, no body),
    // the KR client is not.
    [Test]
    public void NewPeriod_RestoredClient_GetsTheNewSeason() {
        string? before = Environment.GetEnvironmentVariable("MS2_SURVIVAL_CLIENT");
        try {
            foreach (bool restored in new[] { true, false }) {
                Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", restored ? "1" : "0");
                GameSession session = Start(level: 3);
                session.Player.Value.Account.SurvivalSeason = 99;
                Use(session);
                Assert.That(session.Player.Value.Account.SurvivalLevel, Is.EqualTo(2));
                List<byte[]> sent = test.TakeSent();
                Assert.That(sent.Any(packet => packet.SequenceEqual(MapleSurvivalPacket.NewSeason().ToArray())), Is.EqualTo(restored));
            }
            Assert.That(Convert.ToHexString(MapleSurvivalPacket.NewSeason().ToArray()), Is.EqualTo("1901" + "18"));
        } finally {
            Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", before);
        }
    }

    // The restored client's lobby gets the new level and exp (Survival 0x15); the record (0x17) refreshes only the pass
    // window. Before, 0x15 was never sent, so the lobby showed the old level until the next lobby send.
    [Test]
    public void Use_RestoredClient_SendsTheLobbyLevel() {
        string? before = Environment.GetEnvironmentVariable("MS2_SURVIVAL_CLIENT");
        Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", "1");
        try {
            GameSession session = Start();
            Use(session);
            Assert.That(test.TakeSent(), Has.Some.EqualTo(MapleSurvivalPacket.LevelProgress(100, 2, 2).ToArray()));
        } finally {
            Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", before);
        }
    }

    // At the last level the exp stays 0, so the tonic would be lost for nothing (chosen: it is kept); a tonic
    // without its exp is kept too.
    [TestCase("<v SurvivalExp=\"2500\" />", 3)]
    [TestCase("<v SurvivalExp=\"0\" />", 1)]
    [TestCase("<v />", 1)]
    public void Use_KeepsTheTonic(string parameters, int level) {
        GameSession session = Start(parameters, level);
        Use(session);
        Assert.That(session.Item.Inventory.Get(tonic.Uid)!.Amount, Is.EqualTo(2));
        Assert.That(session.Player.Value.Account.SurvivalLevel, Is.EqualTo(level));
    }
}

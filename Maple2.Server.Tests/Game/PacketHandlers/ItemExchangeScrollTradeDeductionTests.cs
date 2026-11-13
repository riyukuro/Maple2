using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using BagItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An exchange scroll row's tradableCountDeduction (itemexchangescrolltable.cpp:110; the client reads it and does not
// use it). Inferred, as the drop table's: the reward's trades left go down by that many. Before, the row's value was
// ingested and ignored, so the reward kept all its trades.
public class ItemExchangeScrollTradeDeductionTests {
    private const int ExchangeId = 77;
    private const int ScrollId = 20000001;
    private const int RewardId = 30000002;

    private TestGameSession test = null!;

    private GameSession Start(int deduction, int rewardTrades) {
        test = new TestGameSession();
        test.SetTable(new ItemExchangeScrollTable(new Dictionary<int, ItemExchangeScrollMetadata> {
            [ExchangeId] = new(new ItemComponent(ScrollId, -1, 1, ItemTag.None), new ItemComponent(RewardId, 4, 1, ItemTag.None), deduction, 0, []),
        }));
        test.Store(TestGameSession.Metadata(ScrollId, type: 1, slotMax: 100,
            function: new ItemMetadataFunction(ItemFunction.ItemExchangeScroll, "ItemExchangeScroll", $"{ExchangeId}", false)));
        ItemMetadata reward = TestGameSession.Metadata(RewardId, type: 1, slotMax: 1);
        test.Register(reward with {
            Limit = reward.Limit with { TransferType = TransferType.Tradable, TradeMaxRarity = 4 },
            Property = reward.Property with { TradableCount = rewardTrades },
        });
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private BagItem? Exchange(GameSession session) {
        long scrollUid = session.Item.Inventory.Find(ScrollId).First().Uid;
        new ItemExchangeScroll { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(scrollUid);
            writer.WriteLong(0);
            writer.WriteInt(1);
        }));
        return session.Item.Inventory.Find(RewardId).FirstOrDefault();
    }

    [TestCase(1, 5, 4)]
    [TestCase(0, 5, 5)]
    [TestCase(3, 2, 0)]
    public void Reward_LosesTheRowsTrades(int deduction, int rewardTrades, int expected) {
        GameSession session = Start(deduction, rewardTrades);
        BagItem? reward = Exchange(session);
        Assert.That(reward, Is.Not.Null);
        Assert.That(reward!.Transfer?.RemainTrades, Is.EqualTo(expected));
    }

    [TestCase(1, 0, 0)]
    [TestCase(2, 7, 5)]
    [TestCase(-1, 7, 7)]
    public void DeductTrades_NotBelowZero(int deduction, int trades, int expected) {
        var transfer = new ItemTransfer(TransferFlag.LimitTrade, trades);
        ItemExchangeScrollRules.DeductTrades(transfer, deduction);
        Assert.That(transfer.RemainTrades, Is.EqualTo(expected));
        Assert.DoesNotThrow(() => ItemExchangeScrollRules.DeductTrades(null, 1));
    }
}

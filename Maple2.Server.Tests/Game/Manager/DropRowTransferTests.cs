using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using GameItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// A box row's tradableCountDeduction takes one trade from the made item (never below none),
// rePackingLimitCountDeduction counts one repack, and isBindCharacter binds it to the opener. KR box 20001016 gives
// 36000617 x5 with all three (the item has no trade count, transfer type 3); NA box 330001701 deducts the one trade
// of accessory 11200072 (transfer type 1).
public class DropRowTransferTests {
    private const int BoxId = 20001016;

    private static IndividualDropItemTable.Item Row(int itemId, int count, bool deductTrade, bool deductRepack, bool bind) =>
        new([itemId], false, 0, 0, 100, new IndividualDropItemTable.Range<int>(count, count), [], 0, 0, deductTrade, deductRepack, bind, false, [], false);

    private static GameItem Open(int itemId, int tradableCount, TransferType transferType, IndividualDropItemTable.Item row, out GameSession session) {
        var test = new TestGameSession();
        ItemMetadata data = TestGameSession.Metadata(itemId, slotMax: 99999);
        test.Register(data with {
            Property = data.Property with { TradableCount = tradableCount },
            Limit = data.Limit with { TransferType = transferType },
        });
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [BoxId] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [1] = new(1, 0, [new IndividualDropItemTable.Entry.DropCount(1, 100)], 0, false, false, [row]),
            },
        }));
        session = test.Start();
        return session.Field!.ItemDrop.GetIndividualDropItems(session, 1, BoxId).Single();
    }

    [Test]
    public void AllThree_NoTradeToTake_RepackCountedAndBound() {
        GameItem item = Open(36000617, 0, (TransferType) 3, Row(36000617, 5, true, true, true), out GameSession session);

        Assert.That(item.Amount, Is.EqualTo(5));
        Assert.That(item.Transfer!.RemainTrades, Is.EqualTo(0));
        Assert.That(item.Transfer.RepackageCount, Is.EqualTo(1));
        Assert.That(item.Transfer.Binding?.CharacterId, Is.EqualTo(session.CharacterId));
    }

    [Test]
    public void TradeDeduction_TakesTheOneTrade() {
        GameItem item = Open(11200072, 1, (TransferType) 1, Row(11200072, 1, true, false, false), out _);

        Assert.That(item.Transfer!.RemainTrades, Is.EqualTo(0));
        Assert.That(item.Transfer.RepackageCount, Is.EqualTo(0));
        Assert.That(item.Transfer.Binding, Is.Null);
    }

    // Without the flags the item keeps its trade.
    [Test]
    public void NoFlags_KeepsTheTrade() {
        GameItem item = Open(11200072, 1, (TransferType) 1, Row(11200072, 1, false, false, false), out _);

        Assert.That(item.Transfer!.RemainTrades, Is.EqualTo(1));
    }
}

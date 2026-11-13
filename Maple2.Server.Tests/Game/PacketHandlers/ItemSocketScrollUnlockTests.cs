using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The socket scroll's unlock (ItemSocketScrollHandler.Unlock): a scroll with tradableCountDeduction leaves the item
// untradable, as the client's dialog warns (s_itemsocket_scroll_tradableCountDeduction_desc).
public class ItemSocketScrollUnlockTests {
    private static ItemSocketScrollMetadata Scroll(int deduction) {
        return new ItemSocketScrollMetadata(MinLevel: 50, MaxLevel: 50, ItemTypes: [12], Rarities: [4], SocketCount: 1,
            TradableCountDeduction: deduction);
    }

    private static Item Accessory(int remainTrades) {
        return new Item(TestGameSession.Metadata(11200001), rarity: 4) {
            Socket = new ItemSocket(3, 0),
            Transfer = new ItemTransfer(TransferFlag.LimitTrade, remainTrades),
        };
    }

    // Before, the scroll took one trade: an item with 3 left kept 2 and could still be traded.
    [Test]
    public void DeductionScroll_LeavesNoTrades() {
        Item item = Accessory(3);
        ItemSocketScrollHandler.Unlock(item, Scroll(1));
        Assert.Multiple(() => {
            Assert.That(item.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(item.Transfer!.RemainTrades, Is.EqualTo(0));
            Assert.That(TradeRules.IsTradeable(item.Transfer.Flag, item.Transfer.RemainTrades, false), Is.False);
        });
    }

    [Test]
    public void PlainScroll_KeepsTrades() {
        Item item = Accessory(3);
        ItemSocketScrollHandler.Unlock(item, Scroll(0));
        Assert.Multiple(() => {
            Assert.That(item.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(item.Transfer!.RemainTrades, Is.EqualTo(3));
        });
    }
}

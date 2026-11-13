using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game;

// The shop row's frame and sale tag (CNpcShopProductCli m_bc and m_c0): the frame picks the row background
// (UIShopDialog::SetProductBackground), the tag is the row's saleTag. Before, the frame went out as 0 (every row
// "normal", the 404 premiumItem rows of shop_game too) and the row's frameType went out in the sale tag's place.
public class ShopItemFrameTests {
    // Id, item id, cost (17), rarity, 500, stock, bought, guild trophy, "" (2), achievement id and rank, championship
    // rank (1) and joins (2), guild npc type (1) and level (2), a bool, sell unit (2).
    private const int FrameOffset = 61;

    private static byte[] Row(bool premium, byte frameType, ShopItemLabel saleTag) {
        var requirements = new ShopItemMetadata.Requirement(0, new ShopItemMetadata.Achievement(0, 0), new ShopItemMetadata.Championship(0, 0),
            new ShopItemMetadata.GuildNpc(default, 0), new ShopItemMetadata.QuestAlliance(default, 0));
        var metadata = new ShopItemMetadata(1, 254, 20000001, 1, new ShopCost { Type = ShopCurrencyType.Meso, Amount = 3000 }, 0, "", requirements,
            null, 1, saleTag, "", false, false, 10000, premium, frameType);
        var shopItem = new ShopItem(metadata) { Item = new Item(TestGameSession.Metadata(20000001)) };
        var writer = new ByteWriter();
        shopItem.WriteTo(writer);
        return writer.ToArray();
    }

    [TestCase(true, (byte) 0, ShopItemLabel.None, (byte) 2)] // premiumItem without a frameType: the premium frame
    [TestCase(false, (byte) 0, ShopItemLabel.None, (byte) 1)] // the client's default for any other row
    [TestCase(false, (byte) 4, ShopItemLabel.HalfPrice, (byte) 4)] // frameType 4 ("hot") stays, the sale tag is its own
    [TestCase(true, (byte) 4, ShopItemLabel.New, (byte) 4)] // a row's frameType wins over premiumItem
    public void FrameAndSaleTag(bool premium, byte frameType, ShopItemLabel saleTag, byte frame) {
        byte[] row = Row(premium, frameType, saleTag);
        Assert.Multiple(() => {
            Assert.That(BitConverter.ToInt16(row, FrameOffset - 2), Is.EqualTo(1)); // the sell unit before it
            Assert.That(row[FrameOffset], Is.EqualTo(frame));
            Assert.That(row[FrameOffset + 1], Is.EqualTo((byte) saleTag));
        });
    }
}

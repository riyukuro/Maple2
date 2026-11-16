using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The shop packet against UIShopDialog::OnShopPacket (uishopdialog.cpp:1524) and its readers: mode 0
// ReadShopInfo (:1553), mode 1 ReadProducts with CNpcShopProductCli::ReadPacket mode 1 (npcshopproductbase.cpp:139),
// mode 4 ReadPurchaseResult, mode 7 ReadRepurchaseAdd with ReadPacket mode 7, mode 8 ReadRepurchaseRemoval.
public class ShopLayoutTests {
    private static ByteReader Reader(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Shop));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    private static ShopItem Row(ShopItemMetadata.Requirement requirements, RestrictedBuyData? restricted) {
        var metadata = new ShopItemMetadata(7, 254, 20000001, 3, new ShopCost { Type = ShopCurrencyType.Meret, Amount = 300, SaleAmount = 250 }, 5, "cat",
            requirements, restricted, 2, ShopItemLabel.New, "icon", true, false, 10000, false, 4) {
        };
        return new ShopItem(metadata) { Item = new Item(TestGameSession.Metadata(20000001)), StockCount = 9, StockPurchased = 2 };
    }

    [Test]
    public void LoadItems_RowFollowsReadPacketMode1() {
        var requirements = new ShopItemMetadata.Requirement(11, new ShopItemMetadata.Achievement(12, 13), new ShopItemMetadata.Championship(14, 15),
            new ShopItemMetadata.GuildNpc((GuildNpcType) 1, 16), new ShopItemMetadata.QuestAlliance((AllianceType) 2, 17));
        ByteReader reader = Reader(ShopPacket.LoadItems([Row(requirements, null)]), 1);
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // product count
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(7)); // serial
            Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) ShopCurrencyType.Meret)); // price: type, item, int, amount, sale
            Assert.That(reader.ReadInt(), Is.Zero);
            Assert.That(reader.ReadInt(), Is.Zero);
            Assert.That(reader.ReadInt(), Is.EqualTo(300));
            Assert.That(reader.ReadInt(), Is.EqualTo(250));
            Assert.That(reader.ReadByte(), Is.EqualTo(3)); // rarity
            Assert.That(reader.ReadInt(), Is.EqualTo(500)); // item count
            Assert.That(reader.ReadInt(), Is.EqualTo(9)); // stock limit
            Assert.That(reader.ReadInt(), Is.EqualTo(2 * 2)); // bought items
            Assert.That(reader.ReadInt(), Is.EqualTo(11)); // guild trophy
            Assert.That(reader.ReadString(), Is.EqualTo("cat"));
            Assert.That(reader.ReadInt(), Is.EqualTo(12));
            Assert.That(reader.ReadInt(), Is.EqualTo(13));
            Assert.That(reader.ReadByte(), Is.EqualTo(14)); // championship rank
            Assert.That(reader.ReadShort(), Is.EqualTo(15));
            Assert.That(reader.ReadByte(), Is.EqualTo(1)); // guild npc type
            Assert.That(reader.ReadShort(), Is.EqualTo(16));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.ReadShort(), Is.EqualTo(2)); // sell unit
            Assert.That(reader.ReadByte(), Is.EqualTo(4)); // frame
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) ShopItemLabel.New)); // sale tag
            Assert.That(reader.ReadString(), Is.EqualTo("icon"));
            Assert.That(reader.ReadShort(), Is.EqualTo(2)); // quest alliance type
            Assert.That(reader.ReadInt(), Is.EqualTo(17));
            Assert.That(reader.ReadBool(), Is.True); // wear for preview
            Assert.That(reader.ReadBool(), Is.False); // no buy period
            Assert.That(reader.ReadBool(), Is.False); // no item life
        });
    }

    [Test]
    public void Purchase_IsItemCountPriceRankAndGuildFlag() {
        var requirements = new ShopItemMetadata.Requirement(0, new ShopItemMetadata.Achievement(0, 0), new ShopItemMetadata.Championship(0, 0),
            new ShopItemMetadata.GuildNpc(default, 0), new ShopItemMetadata.QuestAlliance(default, 0));
        ByteReader reader = Reader(ShopPacket.Buy(Row(requirements, null), 5, 1500, true), 4);
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(1500));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void Repurchase_AddAndRemove() {
        var item = new Item(TestGameSession.Metadata(20000001)) { Rarity = 2 };
        ByteReader add = Reader(ShopPacket.LoadBuyBackItem(new BuyBackItem { Id = 5, Item = item, Price = 99 }), 7);
        Assert.That(add.ReadShort(), Is.EqualTo(1));
        Assert.That(add.ReadInt(), Is.EqualTo(5));
        Assert.That(add.ReadInt(), Is.EqualTo(20000001));
        Assert.That(add.ReadByte(), Is.EqualTo(2));
        Assert.That(add.ReadLong(), Is.EqualTo(99));

        ByteReader remove = Reader(ShopPacket.RemoveBuyBackItem(5), 8);
        Assert.That(remove.ReadInt(), Is.EqualTo(5));
        Assert.That(remove.Available, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Open_HeaderFollowsReadShopInfo(bool reset) {
        var restock = new ShopRestockData(ResetType.Week, ShopCurrencyType.Meso, ShopCurrencyType.Meret, 0, 0, 5000, true, true, true);
        var metadata = new ShopMetadata(31, 77, "ico", (ShopFrameType) 2, true, false, true, true, true, false, true, 123, reset, restock);
        var shop = new Shop(metadata) { RestockCount = 2 };
        ByteReader reader = Reader(ShopPacket.Open(shop, 11000001), 0);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(11000001)); // npc
            Assert.That(reader.ReadInt(), Is.EqualTo(31));
            Assert.That(reader.ReadLong(), Is.EqualTo(123)); // reset time
            Assert.That(reader.ReadInt(), Is.Zero);
            Assert.That(reader.ReadShort(), Is.Zero); // item count
            Assert.That(reader.ReadInt(), Is.EqualTo(77)); // category
            Assert.That(reader.ReadBool(), Is.True); // open wallet
            Assert.That(reader.ReadBool(), Is.True); // sell only
            Assert.That(reader.ReadBool(), Is.EqualTo(reset));
            Assert.That(reader.ReadBool(), Is.True); // no display order sort
            Assert.That(reader.ReadByte(), Is.EqualTo(2)); // shop type
            Assert.That(reader.ReadBool(), Is.True); // only usable
            Assert.That(reader.ReadBool(), Is.False); // hide stats
            Assert.That(reader.ReadBool(), Is.True); // probability
            Assert.That(reader.ReadBool(), Is.False); // new
            Assert.That(reader.ReadString(), Is.EqualTo("ico"));
            if (reset) {
                Assert.That(reader.ReadByte(), Is.EqualTo((byte) shop.RestockCost.Type));
                Assert.That(reader.ReadByte(), Is.EqualTo((byte) ShopCurrencyType.Meret));
                Assert.That(reader.ReadInt(), Is.Zero);
                Assert.That(reader.ReadInt(), Is.EqualTo(shop.RestockCost.Price));
                Assert.That(reader.ReadBool(), Is.True);
                Assert.That(reader.ReadInt(), Is.EqualTo(2));
                Assert.That(reader.ReadByte(), Is.EqualTo((byte) ResetType.Week));
                Assert.That(reader.ReadBool(), Is.True);
                Assert.That(reader.ReadBool(), Is.True);
            }
            Assert.That(reader.Available, Is.Zero);
        });
    }
}

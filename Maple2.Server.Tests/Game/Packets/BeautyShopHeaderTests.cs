using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Beauty sub 0 as UIBeautyShopDialog::OnBeautyShopPacket reads it (uibeautyshopdialog.cpp:1510): the shop
// (CBeautyShopDataCli::Read, npcshopdata.cpp:2134), the style slot count, the saved style count, a long, the items.
public class BeautyShopHeaderTests {
    private static BeautyShop Shop(bool random) {
        var cost = new BeautyShopCostMetadata(ShopCurrencyType.Meret, 170, "", 0);
        return new BeautyShop(new BeautyShopMetadata(508, BeautyShopCategory.Hair, 17, cost, cost, random, false,
            20300246, 20300244, ItemTag.None, [], []), []);
    }

    [TestCase(true, 30, 0)]
    [TestCase(false, 36, 4)]
    public void TheShopCarriesTheRandomFlagAndTheStyleSlotCounts(bool random, int slots, int saved) {
        var reader = new ByteReader(BeautyPacket.BeautyShop(Shop(random), slots, saved).ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Beauty));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        reader.ReadByte(); // shop type
        Assert.That(reader.ReadInt(), Is.EqualTo(508));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) BeautyShopCategory.Hair));
        Assert.That(reader.ReadInt(), Is.EqualTo(20300244));
        Assert.That(reader.ReadBool(), Is.EqualTo(random));
        Assert.That(reader.ReadInt(), Is.EqualTo(20300246));
        Assert.That(reader.ReadInt(), Is.EqualTo(17));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        for (int i = 0; i < 2; i++) {
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) ShopCurrencyType.Meret));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(170));
            Assert.That(reader.ReadString(), Is.EqualTo(""));
        }
        Assert.That(reader.ReadByte(), Is.EqualTo(slots));
        Assert.That(reader.ReadByte(), Is.EqualTo(saved));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void SlotCountsAreClampedToAByte() {
        byte[] packet = BeautyPacket.BeautyShop(Shop(false), 300, -1).ToArray();
        Assert.That(packet[^12], Is.EqualTo(255));
        Assert.That(packet[^11], Is.EqualTo(0));
    }
}

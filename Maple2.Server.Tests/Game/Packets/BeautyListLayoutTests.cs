using System.Collections.Generic;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// Beauty (0x31) modes beside the shop page, against the three registered readers: UIBeautyShopStyleDialog::FUN_140f05a80
// (uibeautyshopstyledialog.cpp, the saved style window) for 2, 13, 14, 15, 16, 17, 18, 20, 21; UIItemColoringDialog::
// OnBeautyShopPacket (uiitemcoloringdialog.cpp) for 1; UIBeautyShopDialog::OnBeautyShopPacket for 0 (BeautyShopHeaderTests).
// 1 and 2: u8 shop type, the shop block (CBeautyShopDataCli::Read); 13: none; 14: u16 count; 15: u16 count x (int item id,
// long uid, int slot, long created, the hair proxy block); 16 and 17: long uid, long copy uid, u8 slot, long created;
// 18: long uid; 20: u8, u16 extended slots; 21: none.
public class BeautyListLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Beauty));
        return reader;
    }

    private static BeautyShop Shop() {
        var cost = new BeautyShopCostMetadata(ShopCurrencyType.Meret, 170, "", 0);
        return new BeautyShop(new BeautyShopMetadata(508, BeautyShopCategory.Hair, 17, cost, cost, false, false,
            20300246, 20300244, ItemTag.None, [], []), []);
    }

    [Test]
    public void DyeAndSaveShop_CarryTheShopBlockOfSub0() {
        var shop = new ByteWriter();
        shop.WriteClass<BeautyShop>(Shop());
        byte[] expected = shop.ToArray();

        ByteReader reader = Read(BeautyPacket.DyeShop(Shop()));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(expected));

        reader = Read(BeautyPacket.SaveShop(Shop()));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(expected));
    }

    [Test]
    public void HairList() {
        ByteReader reader = Read(BeautyPacket.StartList());
        Assert.That(reader.ReadByte(), Is.EqualTo(13));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(BeautyPacket.ListCount(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(14));
        Assert.That(reader.ReadShort(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));

        var hair = new Item(TestGameSession.Metadata(10200001), 1, 1) { Uid = 7001, CreationTime = 1700000000 };
        hair.Appearance = new HairAppearance(default(EquipColor));
        reader = Read(BeautyPacket.ListHair(new List<Item> { hair }));
        Assert.That(reader.ReadByte(), Is.EqualTo(15));
        Assert.That(reader.ReadShort(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(10200001));
        Assert.That(reader.ReadLong(), Is.EqualTo(7001));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        var proxy = new ByteWriter();
        proxy.WriteClass<HairAppearance>((HairAppearance) hair.Appearance);
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(proxy.ToArray()));
    }

    [Test]
    public void SaveDeleteSlotsApply() {
        var current = new Item(TestGameSession.Metadata(10200001), 1, 1) { Uid = 7001 };
        var copy = new Item(TestGameSession.Metadata(10200001), 1, 1) { Uid = 7002, CreationTime = 1700000000 };
        foreach ((bool extended, byte mode) in new[] { (false, (byte) 16), (true, (byte) 17) }) {
            ByteReader reader = Read(BeautyPacket.SaveHair(current, copy, extended));
            Assert.That(reader.ReadByte(), Is.EqualTo(mode));
            Assert.That(reader.ReadLong(), Is.EqualTo(7001));
            Assert.That(reader.ReadLong(), Is.EqualTo(7002));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
            Assert.That(reader.Available, Is.EqualTo(0));
        }

        ByteReader del = Read(BeautyPacket.DeleteHair(7002));
        Assert.That(del.ReadByte(), Is.EqualTo(18));
        Assert.That(del.ReadLong(), Is.EqualTo(7002));
        Assert.That(del.Available, Is.EqualTo(0));

        ByteReader slots = Read(BeautyPacket.SaveSlots(6));
        Assert.That(slots.ReadByte(), Is.EqualTo(20));
        Assert.That(slots.ReadByte(), Is.EqualTo(0));
        Assert.That(slots.ReadShort(), Is.EqualTo(6));
        Assert.That(slots.Available, Is.EqualTo(0));

        ByteReader apply = Read(BeautyPacket.ApplySavedHair());
        Assert.That(apply.ReadByte(), Is.EqualTo(21));
        Assert.That(apply.Available, Is.EqualTo(0));
    }
}

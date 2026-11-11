using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ShopErrorTests {
    // Client: mode 15 (client/ms2/uishopdialog.cpp:1858) reads an int code, a byte and an int, then
    // switches on the code.
    [TestCase(ShopError.s_err_lack_shopitem, 2)]
    [TestCase(ShopError.s_err_invalid_item, 7)]
    [TestCase(ShopError.s_msg_cant_sell, 10)]
    [TestCase(ShopError.s_err_lack_meso, 11)]
    [TestCase(ShopError.s_err_lack_merat, 12)]
    [TestCase(ShopError.s_err_inventory, 15)]
    [TestCase(ShopError.s_err_lack_guild_trophy, 16)]
    [TestCase(ShopError.s_err_lack_payment_item, 18)]
    [TestCase(ShopError.s_err_lack_guild_require_date, 20)]
    [TestCase(ShopError.s_anti_addiction_cannot_receive, 23)]
    [TestCase(ShopError.s_msg_cant_sell_to_only_sell_shop, 24)]
    [TestCase(ShopError.s_system_property_protection_time, 25)]
    [TestCase(ShopError.s_guild_err_buy_no_master, 26)]
    [TestCase(ShopError.s_guild_err_not_enough_guild_fund, 27)]
    [TestCase(ShopError.s_err_invalid_item_cannot_buy_by_period, 28)]
    [TestCase(ShopError.s_shop_no_star_point_event, 30)]
    [TestCase(ShopError.s_meratmarket_error_country_limit, 32)]
    [TestCase(ShopError.s_err_cannot_sell_petitem_summon, 33)]
    public void ClientCodes(ShopError error, int code) {
        ByteWriter writer = ShopPacket.Error(error, 0, 20000001);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Shop));
        Assert.That(reader.ReadByte(), Is.EqualTo(15));
        Assert.That(reader.ReadInt(), Is.EqualTo(code));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 1 + 4));
    }
}

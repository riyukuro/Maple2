using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A request is taken only in the shop whose dialog sends it. The saved style list (Beauty 16-19, 21) is the style save
// dialog's (type 4): a style saved, deleted or applied in another shop used that shop (the KR hair shop 504's style
// fee is 0, the style save shop 510's is not). A shop opened from a beauty coupon takes its row's type, as an npc
// with no dialog function does: a random row's coupon opened a style shop, where the random pick is refused.
public class BeautyShopKindTests {
    private const int Hair = 10200001;
    private const int Coupon = 20300100;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private BeautyHandler Handler() => new() {
        ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
        ServerTableMetadata = test.ServerTableMetadata,
    };

    private static BeautyShopMetadata Row(int id, bool random, params BeautyShopItem[] items) {
        var fee = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        return new BeautyShopMetadata(id, BeautyShopCategory.Hair, 0, fee, fee, random, false, 0, 0, ItemTag.None, items, []);
    }

    [TestCase(BeautyShopType.Default, 0)]
    [TestCase(BeautyShopType.Random, 0)]
    [TestCase(BeautyShopType.Modify, 0)]
    [TestCase(BeautyShopType.Save, 1)]
    public void AStyleIsSavedOnlyInAStyleSaveShop(BeautyShopType type, int saved) {
        test = new TestGameSession();
        Item worn = test.Store(TestGameSession.Metadata(Hair, EquipSlot.HR), 1, ItemGroup.Outfit, EquipSlot.HR);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        session.Beauty = new BeautyManager(session);
        session.BeautyShop = new BeautyShop(Row(510, false), []) { Type = type };

        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(16);
            writer.WriteLong(worn.Uid);
        }));

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(saved));
    }

    // Beauty 23 with a coupon of shop 600 (function ItemChangeBeauty, as the KR coupons of shops 601-901 have).
    [TestCase(true, BeautyShopType.Random)]
    [TestCase(false, BeautyShopType.Default)]
    public void ACouponShopTakesItsRowsType(bool random, BeautyShopType type) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Hair, EquipSlot.HR));
        Item coupon = test.Store(TestGameSession.Metadata(Coupon,
            function: new ItemMetadataFunction(ItemFunction.ItemChangeBeauty, "ItemChangeBeauty", "600", false)));
        var row = new BeautyShopItem(Hair, new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0), 1, 0, 0, 0, ShopItemLabel.None);
        test.SetServerTable(new BeautyShopTable(new Dictionary<int, BeautyShopMetadata> { [600] = Row(600, random, row) }));
        test.Start();
        session.Beauty = new BeautyManager(session);

        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(23);
            writer.WriteLong(coupon.Uid);
        }));

        Assert.That(session.BeautyShop?.Type, Is.EqualTo(type));
    }
}

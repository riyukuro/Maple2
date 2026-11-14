using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A random hair pick (Beauty 7) is sent only from a random shop's dialog (type 2, uibeautyshopdialog.cpp:1841). In a
// style shop it took the shop's style fee (0 in the KR hair shop 504) and picked one of its rows.
public class BeautyRandomHairTests {
    private const int ShopId = 504;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(BeautyShopType type) {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 0, string.Empty, 0);
        var row = new BeautyShopItem(10200001, style, 1, 0, 0, 0, ShopItemLabel.None);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(ShopId, BeautyShopCategory.Hair, 0, style, color, false, false, 0, 0,
            ItemTag.None, [row], []), [row]) {
            Type = type,
        };
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    // Beauty 7: the shop id, no voucher, then the long the shop answer carried.
    private void Pick() {
        new BeautyHandler {
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(7);
            writer.WriteInt(ShopId);
            writer.WriteBool(false);
            writer.WriteLong(0);
        }));
    }

    [TestCase(BeautyShopType.Default)]
    [TestCase(BeautyShopType.Modify)]
    [TestCase(BeautyShopType.Save)]
    public void APickOutsideARandomShopIsRefused(BeautyShopType type) {
        Start(type);

        Pick();

        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
    }

    [Test]
    public void APickInARandomShopTakesTheStyleFee() {
        Start(BeautyShopType.Random);

        Pick();

        Assert.That(session.Currency.Meso, Is.EqualTo(4700L));
    }

    // Beauty 12 "keep the old hair" with no random pick waiting gives nothing; it gave the shop's return coupon each
    // time it was sent.
    [Test]
    public void AKeepAnswerWithNoPickGivesNoCoupon() {
        const int coupon = 20300200;
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(coupon));
        test.SetTable(new ItemOptionPickTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>()));
        test.SetTable(new ItemOptionConstantTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>()));
        test.SetTable(new ItemOptionStaticTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        session.Beauty = new BeautyManager(session);
        var fee = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(508, BeautyShopCategory.Hair, 0, fee, fee, true, false, coupon, 0,
            ItemTag.None, [], []), []) {
            Type = BeautyShopType.Random,
        };
        Assert.That(session.Field!.ItemDrop.CreateItem(coupon), Is.Not.Null);

        for (int i = 0; i < 3; i++) {
            new BeautyHandler {
                ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
                ServerTableMetadata = test.ServerTableMetadata,
            }.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(12);
                writer.WriteBool(false);
            }));
        }

        Assert.That(session.Item.Inventory.Filter(item => item.Id == coupon), Is.Empty);
    }
}

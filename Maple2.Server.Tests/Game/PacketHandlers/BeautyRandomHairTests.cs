using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
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
}

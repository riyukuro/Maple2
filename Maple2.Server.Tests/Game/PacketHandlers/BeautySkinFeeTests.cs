using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A skin color change (Beauty 6) takes the shop's recolor fee. It took the style fee: the KR skin shop 501 has none
// and a recolor fee of 150 merets, so the change was free.
public class BeautySkinFeeTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private static readonly SkinColor Skin = new(new Color(10, 20, 30, 255));

    private void Change(BeautyShopCategory category) {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(501, category, 0, style, color, false, false, 0, 0, ItemTag.None, [], []), []) {
            Type = category == BeautyShopCategory.Skin ? BeautyShopType.Modify : BeautyShopType.Default,
        };
        session.Currency.Meso = 5000;
        SkinColor skin = Skin;

        new BeautyHandler {
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteByte(0);
            writer.Write<SkinColor>(skin);
            writer.WriteBool(false);
        }));
    }

    [Test]
    public void ASkinChangeTakesTheRecolorFee() {
        Change(BeautyShopCategory.Skin);

        Assert.That((session.Player.Value.Character.SkinColor, session.Currency.Meso), Is.EqualTo((Skin, 4700L)));
    }

    // The skin page is the skin shop's: another shop's recolor fee (the KR makeup shop 505: 20 merets, the skin shop
    // 501: 150) does not pay a skin color.
    [TestCase(BeautyShopCategory.Makeup)]
    [TestCase(BeautyShopCategory.Hair)]
    [TestCase(BeautyShopCategory.Dye)]
    public void ASkinChangeOutsideTheSkinShopIsRefused(BeautyShopCategory category) {
        Change(category);

        Assert.That((session.Player.Value.Character.SkinColor, session.Currency.Meso), Is.Not.EqualTo((Skin, 4700L)));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
    }
}

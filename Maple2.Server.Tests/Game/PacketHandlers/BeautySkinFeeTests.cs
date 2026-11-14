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

    [Test]
    public void ASkinChangeTakesTheRecolorFee() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(501, default, 0, style, color, false, false, 0, 0, ItemTag.None, [], []), []) {
            Type = BeautyShopType.Modify,
        };
        session.Currency.Meso = 5000;
        var skin = new SkinColor(new Color(10, 20, 30, 255));

        new BeautyHandler {
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteByte(0);
            writer.Write<SkinColor>(skin);
            writer.WriteBool(false);
        }));

        Assert.That((session.Player.Value.Character.SkinColor, session.Currency.Meso), Is.EqualTo((skin, 4700L)));
    }
}

using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A beauty fee is checked and taken in one step, and a price below 0 is refused. The meso fee was checked with
// CanAddMeso(-price) and taken with ChangeMeso(-price), the other wallets with `held < price` and
// Change(type, -price): a negative price passed the check and the take added it.
public class BeautyNegativePriceTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private static readonly SkinColor Skin = new(new Color(10, 20, 30, 255));

    private void Change(ShopCurrencyType currency, int price) {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        test.SetServerTable(new ConstantsTable(KarmaTokenMax: 30000));
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var color = new BeautyShopCostMetadata(currency, price, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(501, BeautyShopCategory.Skin, 0, style, color, false, false, 0, 0, ItemTag.None, [], []), []) {
            Type = BeautyShopType.Modify,
        };
        session.Currency.Meso = 5000;
        session.Currency[CurrencyType.Treva] = 1000;
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

    private (bool, long, long) After() => (session.Player.Value.Character.SkinColor.Equals(Skin), session.Currency.Meso, session.Currency[CurrencyType.Treva]);

    [TestCase(ShopCurrencyType.Meso)]
    [TestCase(ShopCurrencyType.Treva)]
    public void ANegativeFee_IsRefusedAndChangesNothing(ShopCurrencyType currency) {
        Change(currency, -300);

        Assert.That(After(), Is.EqualTo((false, 5000L, 1000L)));
    }

    [Test]
    public void ATrevaFee_IsTaken() {
        Change(ShopCurrencyType.Treva, 300);

        Assert.That(After(), Is.EqualTo((true, 5000L, 700L)));
    }

    [Test]
    public void AFeeOverTheBalance_IsRefusedAndChangesNothing() {
        Change(ShopCurrencyType.Treva, 1001);

        Assert.That(After(), Is.EqualTo((false, 5000L, 1000L)));
    }
}

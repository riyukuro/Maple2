using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A beauty shop item locked by a trophy grade or a level: the client shows it locked and keeps the save button off
// (uibeautyshopstyledialog.cpp:709, uibeautyshopdialog.cpp:1412-1421), so a new item is not sold before the grade
// is cleared or the level reached. The KR shops lock 40 items by a trophy grade and 16 by level. The ids are made up.
public class BeautyShopLockTests {
    private const int Shop = 500;
    private const int Eyes = 10300901;
    private const int Trophy = 23100901;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(int achievementId, byte rank, short requiredLevel) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Eyes, EquipSlot.FA));
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [1, 2]));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var fee = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var item = new BeautyShopItem(Eyes, fee, 1, achievementId, rank, requiredLevel, default);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(Shop, default, 0, fee, fee, false, false, 0, 0, ItemTag.None, [], []), [item]);
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    // Beauty 3: the shop, no voucher, the item, the shop-open value, then the item's color.
    private void Buy() {
        new BeautyHandler {
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteByte(0);
            writer.WriteBool(false);
            writer.WriteInt(Eyes);
            writer.WriteLong(0);
            writer.WriteBytes(new byte[20]);
        }));
    }

    private bool Wears() => session.Item.Equips.Get(EquipSlot.FA)?.Id == Eyes;

    [Test]
    public void ATrophyLockedItemIsNotSoldBeforeTheGrade() {
        Start(Trophy, 2, 0);
        session.ConditionUpdate(ConditionType.home_goto);

        Buy();

        Assert.That((Wears(), session.Currency.Meso), Is.EqualTo((false, 5000)));
    }

    [Test]
    public void ATrophyLockedItemIsSoldAtTheGrade() {
        Start(Trophy, 2, 0);
        session.ConditionUpdate(ConditionType.home_goto);
        session.ConditionUpdate(ConditionType.home_goto);

        Buy();

        Assert.That((Wears(), session.Currency.Meso), Is.EqualTo((true, 4000)));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void ALevelLockedItemIsNotSoldBelowTheLevel() {
        Start(0, 0, 60);
        session.Player.Value.Character.Level = 59;

        Buy();

        Assert.That((Wears(), session.Currency.Meso), Is.EqualTo((false, 5000)));

        session.Player.Value.Character.Level = 60;
        Buy();
        Assert.That((Wears(), session.Currency.Meso), Is.EqualTo((true, 4000)));
    }
}

using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// An edit of a worn hair, face or makeup item (Beauty 5) costs what the client shows for it (UIBeautyShopDialog::GetFee,
// uibeautyshopdialog.cpp:1282-1320): the shop's recolor fee when the item's color changed, else nothing. It took the
// style fee: the KR hair and makeup shops 504 and 505 have a style fee of 0 and recolor fees of 170 and 20 merets.
public class BeautyRecolorFeeTests {
    private const int Makeup = 10300901;
    private const int Trophy = 23100902;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item worn = null!;

    private void Start(BeautyShopType type) {
        test = new TestGameSession();
        worn = test.Store(TestGameSession.Metadata(Makeup, EquipSlot.FA), 1, ItemGroup.Outfit, EquipSlot.FA);
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.beauty_change_color, [1, 2]));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(505, default, 0, style, color, false, false, 0, 0, ItemTag.None, [], []), []) {
            Type = type,
        };
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    // Beauty 5: the shop, no voucher, the worn item's uid, then its new appearance.
    private void Edit(EquipColor color) {
        new BeautyHandler {
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteByte(0);
            writer.WriteBool(false);
            writer.WriteLong(worn.Uid);
            writer.WriteClass<ItemAppearance>(new ItemAppearance(color));
        }));
    }

    private static readonly EquipColor Red = new(new Color(0, 0, 255, 255), new Color(0, 0, 255, 255), new Color(0, 0, 255, 255), 1, 3);

    private EquipColor? Wears() => session.Item.Equips.Get(EquipSlot.FA)?.Appearance?.Color;

    [TestCase(BeautyShopType.Default)]
    [TestCase(BeautyShopType.Modify)]
    public void ARecolorTakesTheRecolorFee(BeautyShopType type) {
        Start(type);

        Edit(Red);

        Assert.That((Wears(), session.Currency.Meso, test.TrophyCounter(Trophy)), Is.EqualTo(((EquipColor?) Red, 4700L, (long?) 1)));
    }

    [Test]
    public void AnEditKeepingTheColorIsFree() {
        Start(BeautyShopType.Default);
        EquipColor same = worn.Appearance!.Color;

        Edit(same);

        Assert.That(session.Item.Equips.Get(EquipSlot.FA)?.Uid, Is.Not.EqualTo(worn.Uid));
        Assert.That((session.Currency.Meso, test.TrophyCounter(Trophy) ?? 0), Is.EqualTo((5000L, 0L)));
    }

    // A random shop's client never sends 5: refused, not free. A shop of type 4 shows no fee: free, and not counted.
    [Test]
    public void AnEditInARandomShopIsRefused() {
        Start(BeautyShopType.Random);

        Edit(Red);

        Assert.That((session.Item.Equips.Get(EquipSlot.FA)?.Uid, session.Currency.Meso), Is.EqualTo(((long?) worn.Uid, 5000L)));
    }

    [Test]
    public void ARecolorInASaveShopIsFreeAndNotCounted() {
        Start(BeautyShopType.Save);

        Edit(Red);

        Assert.That((Wears(), session.Currency.Meso, test.TrophyCounter(Trophy) ?? 0), Is.EqualTo(((EquipColor?) Red, 5000L, 0L)));
    }

    [Test]
    public void ARecolorWithoutTheFeeIsRefused() {
        Start(BeautyShopType.Default);
        session.Currency.Meso = 200;

        Edit(Red);

        Assert.That((session.Item.Equips.Get(EquipSlot.FA)?.Uid, session.Currency.Meso), Is.EqualTo(((long?) worn.Uid, 200L)));
    }
}

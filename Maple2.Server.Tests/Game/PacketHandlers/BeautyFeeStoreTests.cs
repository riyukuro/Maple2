using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A beauty fee is stored in the save that stores what it paid for (the new item, the skin color): the fee was taken
// and stored on its own first, so a failed item or skin write kept the money, or a crash between them lost it.
public class BeautyFeeStoreTests {
    private const int Face = 10300901;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item worn = null!;

    private static readonly SkinColor Skin = new(new Color(10, 20, 30, 255));
    private static readonly EquipColor Red = new(new Color(0, 0, 255, 255), new Color(0, 0, 255, 255), new Color(0, 0, 255, 255), 1, 3);

    private void Start(BeautyShopCategory category, BeautyShopType type) {
        test = new TestGameSession();
        worn = test.Store(TestGameSession.Metadata(Face, EquipSlot.FA), 1, ItemGroup.Outfit, EquipSlot.FA);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(501, category, 0, style, color, false, false, 0, 0, ItemTag.None, [], []), []) {
            Type = type,
        };
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    private BeautyHandler Handler() => new() {
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        NpcMetadata = null!,
        ServerTableMetadata = test.ServerTableMetadata,
    };

    // Beauty 6: the skin page's new color, no voucher.
    private void ChangeSkin(bool failSave) {
        Start(BeautyShopCategory.Skin, BeautyShopType.Modify);
        SkinColor skin = Skin;
        if (failSave) {
            test.FailSave = _ => true;
        }
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteByte(0);
            writer.Write<SkinColor>(skin);
            writer.WriteBool(false);
        }));
        test.FailSave = null;
    }

    // Beauty 5: a recolor of the worn face.
    private void Recolor(bool failSave) {
        Start(BeautyShopCategory.Face, BeautyShopType.Default);
        if (failSave) {
            test.FailSave = _ => true;
        }
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteByte(0);
            writer.WriteBool(false);
            writer.WriteLong(worn.Uid);
            writer.WriteClass<ItemAppearance>(new ItemAppearance(Red));
        }));
        test.FailSave = null;
    }

    private (SkinColor Skin, long Meso) Stored() {
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.Character row = context.Character.AsNoTracking().Single(character => character.Id == test.CharacterId);
        return (row.SkinColor, row.Currency.Meso);
    }

    [Test]
    public void ASkinChangeIsStoredWithItsFee() {
        ChangeSkin(failSave: false);

        Assert.That((session.Player.Value.Character.SkinColor, session.Currency.Meso), Is.EqualTo((Skin, 4700L)));
        Assert.That(Stored(), Is.EqualTo((Skin, 4700L)));
    }

    [Test]
    public void ARefusedSkinSaveKeepsTheFeeAndTheOldColor() {
        ChangeSkin(failSave: true);

        Assert.That(session.Player.Value.Character.SkinColor, Is.Not.EqualTo(Skin));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
        Assert.That(Stored().Meso, Is.Not.EqualTo(4700L));
    }

    [Test]
    public void ARecolorIsStoredWithItsFee() {
        Recolor(failSave: false);

        Item? now = session.Item.Equips.Get(EquipSlot.FA);
        Assert.That((now?.Appearance?.Color, session.Currency.Meso), Is.EqualTo(((EquipColor?) Red, 4700L)));
        Assert.That(Stored().Meso, Is.EqualTo(4700L));
        using var context = new Ms2Context(test.Options);
        Assert.That(context.Item.AsNoTracking().Any(item => item.Id == now!.Uid), Is.True);
    }

    [Test]
    public void ARefusedRecolorSaveKeepsTheFeeAndTheWornFace() {
        Recolor(failSave: true);

        Assert.That((session.Item.Equips.Get(EquipSlot.FA)?.Uid, session.Currency.Meso), Is.EqualTo(((long?) worn.Uid, 5000L)));
        using var context = new Ms2Context(test.Options);
        Assert.That(context.Item.AsNoTracking().Count(item => item.ItemId == Face), Is.EqualTo(1));
    }

    // The coupon box result (s2c Beauty 12) is read as an int coupon id and a mail flag byte
    // (uibeautyshopdialog.cpp:1557): the flag was missing.
    [Test]
    public void TheCouponResultCarriesTheMailFlag() {
        Assert.That(Convert.ToHexString(BeautyPacket.RandomHairResult(20300246, mailed: true).ToArray()[2..]), Is.EqualTo("0CD6C1350101"));
        Assert.That(Convert.ToHexString(BeautyPacket.RandomHairResult(0).ToArray()[2..]), Is.EqualTo("0C0000000000"));
    }

    [TestCase(0.0)]
    [TestCase(0.5)]
    [TestCase(0.999999)]
    public void TheRandomScaleStaysInItsRange(double roll) {
        double scale = BeautyHandler.RandomScale(0.8f, 1.2f, roll);
        Assert.That(scale, Is.InRange(0.8f, 1.2f));
    }
}

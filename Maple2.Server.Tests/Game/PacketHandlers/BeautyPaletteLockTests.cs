using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A palette color locked by a trophy grade (color palette rows with achieveID and achieveGrade): the client refuses
// to save it before the grade (IsPaletteColorLocked, uibeautyshopstyledialog.cpp:748; uibeautyshopdialog.cpp:1419;
// uiitemcoloringdialog.cpp:1004), so the server refuses it with the color error and takes no fee. Exactly as strict as
// the client: an unlocked row, a row of a trophy the client has no data for and the item's default color pass. The
// palette, color and trophy ids are made up.
public class BeautyPaletteLockTests {
    private const int Eyes = 10300901;
    private const int Top = 12220308;
    private const int Palette = 9001;
    private const int OpenColor = 1;
    private const int LockedColor = 2;
    private const int UnknownTrophyColor = 3;
    private const int Trophy = 23100902;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item wornTop = null!;
    private Item wornEyes = null!;

    private void Start(BeautyShopCategory category = BeautyShopCategory.Face, int defaultIndex = 0) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Eyes, EquipSlot.FA) with { Customize = new ItemMetadataCustomize(Palette, defaultIndex) });
        wornTop = test.Store(TestGameSession.Metadata(Top, EquipSlot.CL), 1, ItemGroup.Gear, EquipSlot.CL);
        wornEyes = test.Store(TestGameSession.Metadata(Eyes + 1, EquipSlot.FA), 1, ItemGroup.Outfit, EquipSlot.FA);
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [1, 2]));
        Color white = default;
        test.SetTable(new ColorPaletteTable(new Dictionary<int, IReadOnlyDictionary<int, ColorPaletteTable.Entry>> {
            [Palette] = new Dictionary<int, ColorPaletteTable.Entry> {
                [OpenColor] = new(white, white, white, 0, 0),
                [LockedColor] = new(white, white, white, Trophy, 2),
                [UnknownTrophyColor] = new(white, white, white, 23109999, 1),
            },
        }));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        wornEyes.Appearance = new ItemAppearance(new EquipColor(white, white, white, Palette, OpenColor));
        var fee = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var row = new BeautyShopItem(Eyes, fee, 1, 0, 0, 0, default);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(500, category, 0, fee, fee, false, false, 0, 0, ItemTag.None, [], []), [row]) {
            Type = category == BeautyShopCategory.Dye ? BeautyShopType.Modify : BeautyShopType.Default,
        };
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    private static void WriteColor(IByteWriter writer, int index) => writer.Write<EquipColor>(new EquipColor(default, default, default, Palette, index));

    private void Send(byte mode, System.Action<IByteWriter> payload) {
        new BeautyHandler {
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            payload(writer);
        }));
    }

    // Beauty 3: the shop, no voucher, the item, the shop-open value, then the color.
    private void Buy(int index) => Send(3, writer => {
        writer.WriteByte(0);
        writer.WriteBool(false);
        writer.WriteInt(Eyes);
        writer.WriteLong(0);
        WriteColor(writer, index);
    });

    // Beauty 5: the worn eyes recolored.
    private void Recolor(int index) => Send(5, writer => {
        writer.WriteByte(0);
        writer.WriteBool(false);
        writer.WriteLong(wornEyes.Uid);
        WriteColor(writer, index);
    });

    // Beauty 22, one entry: the worn top dyed.
    private void Dye(int index) => Send(22, writer => {
        writer.WriteByte(1);
        writer.WriteBool(true);
        writer.WriteByte(0);
        writer.WriteBool(false);
        writer.WriteByte(0);
        writer.WriteInt(0);
        writer.WriteLong(0);
        writer.WriteLong(wornTop.Uid);
        writer.WriteInt(Top);
        WriteColor(writer, index);
    });

    private bool SentColorError() {
        byte[] expected = BeautyPacket.Error(BeautyError.s_beauty_msg_error_color).ToArray();
        return test.TakeSent().Any(packet => packet.SequenceEqual(expected));
    }

    private bool WearsNewEyes() => session.Item.Equips.Get(EquipSlot.FA)?.Id == Eyes;

    [Test]
    public void Create_InALockedColor_IsRefusedBeforeTheGrade() {
        Start();
        session.ConditionUpdate(ConditionType.home_goto);

        Buy(LockedColor);

        Assert.That((WearsNewEyes(), session.Currency.Meso, SentColorError()), Is.EqualTo((false, 5000L, true)));
    }

    [Test]
    public void Create_InALockedColor_IsSoldAtTheGrade() {
        Start();
        session.ConditionUpdate(ConditionType.home_goto);
        session.ConditionUpdate(ConditionType.home_goto);

        Buy(LockedColor);

        Assert.That((WearsNewEyes(), session.Currency.Meso), Is.EqualTo((true, 4000L)));
    }

    // The client counts a trophy it has no data for as unlocked.
    [TestCase(OpenColor)]
    [TestCase(UnknownTrophyColor)]
    public void Create_InAnUnlockedColor_IsSold(int index) {
        Start();

        Buy(index);

        Assert.That((WearsNewEyes(), session.Currency.Meso), Is.EqualTo((true, 4000L)));
    }

    // The item's own default color is never the dialog's pick, so it is not checked.
    [Test]
    public void Create_InTheItemsDefaultColor_IsSold() {
        Start(defaultIndex: LockedColor);

        Buy(LockedColor);

        Assert.That((WearsNewEyes(), session.Currency.Meso), Is.EqualTo((true, 4000L)));
    }

    [Test]
    public void Recolor_ToALockedColor_IsRefused() {
        Start();

        Recolor(LockedColor);

        Assert.That((session.Item.Equips.Get(EquipSlot.FA)?.Uid, session.Currency.Meso, SentColorError()), Is.EqualTo((wornEyes.Uid, 5000L, true)));
    }

    [Test]
    public void Dye_InALockedColor_IsRefusedAndTheItemKeepsItsColor() {
        Start(BeautyShopCategory.Dye);
        ItemAppearance? before = wornTop.Appearance;

        Dye(LockedColor);

        Assert.That((session.Currency.Meso, SentColorError()), Is.EqualTo((5000L, true)));
        Assert.That(wornTop.Appearance, Is.SameAs(before));
    }

    [Test]
    public void Dye_InAnOpenColor_IsPaid() {
        Start(BeautyShopCategory.Dye);

        Dye(OpenColor);

        Assert.That(session.Currency.Meso, Is.EqualTo(4000L));
    }
}

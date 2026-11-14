using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A gear dye (Beauty 22) counts for beauty_change_color: KMS2 sends the update of trophy 23100191 (items 11050001 to
// 15999999) right after a dye of 12220308. Only a dye that is paid counts.
public class BeautyGearDyeTests {
    private const int Top = 12220308;
    private const int Trophy = 23100191;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item worn = null!;

    [SetUp]
    public void Start() {
        test = new TestGameSession();
        worn = test.Store(TestGameSession.Metadata(Top, EquipSlot.CL), 1, ItemGroup.Gear, EquipSlot.CL);
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.beauty_change_color, [1, 2], codeRange: (11050001, 15999999)));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var fee = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(506, default, 0, fee, fee, false, false, 0, 0, ItemTag.None, [], []), []) {
            Type = BeautyShopType.Modify,
        };
        test.TakeSent();
    }

    // Beauty 22, one entry: the head (ReadDyeHead), then the item's colors.
    private void Dye(long uid, int itemId) {
        new BeautyHandler {
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(22);
            writer.WriteByte(1);
            writer.WriteBool(true);
            writer.WriteByte(0);
            writer.WriteBool(false);
            writer.WriteByte(0);
            writer.WriteInt(0);
            writer.WriteLong(0);
            writer.WriteLong(uid);
            writer.WriteInt(itemId);
            writer.WriteBytes(new byte[20]);
        }));
    }

    [Test]
    public void APaidDyeCounts() {
        session.Currency.Meso = 5000;

        Dye(worn.Uid, Top);

        Assert.That((session.Currency.Meso, test.TrophyCounter(Trophy)), Is.EqualTo((4000, 1L)));
    }

    [Test]
    public void AnUnpaidDyeDoesNotCount() {
        session.Currency.Meso = 500;

        Dye(worn.Uid, Top);

        Assert.That((session.Currency.Meso, test.TrophyCounter(Trophy) ?? 0), Is.EqualTo((500, 0L)));
    }

    [Test]
    public void ARefusedEntryDoesNotCount() {
        session.Currency.Meso = 5000;

        Dye(worn.Uid, Top + 1);

        Assert.That((session.Currency.Meso, test.TrophyCounter(Trophy) ?? 0), Is.EqualTo((5000, 0L)));
    }
}

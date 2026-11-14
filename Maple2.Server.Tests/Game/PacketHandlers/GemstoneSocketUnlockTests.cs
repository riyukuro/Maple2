using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketSystem 0, a socket unlock (RequestUnlock). An item with no skin type takes exactly 2 copies of itself
// with as many open sockets (1 with ItemSocketExpansion100 off) and crystal fragments by its level and rarity (a level 0,
// rarity 1 item: 200); fragments short are paid at 1000 mesos each, and the ReduceGameItemSocketUnlockFee
// attribute lowers the count.
public class GemstoneSocketUnlockTests {
    private TestGameSession test = null!;
    private Item gear = null!;
    private Item[] materials = [];
    private Item? chips;

    private GameSession Start(int chipCount, long mesos, int materialCount = 2, int skinType = 0) {
        test = new TestGameSession();
        ItemMetadata gearData = TestGameSession.Metadata(11200001, type: 1);
        gearData = gearData with { Property = gearData.Property with { SkinType = skinType } };
        gear = test.Store(gearData);
        materials = Enumerable.Range(0, materialCount).Select(_ => test.Store(gearData)).ToArray();
        if (chipCount > 0) {
            ItemMetadata chipData = TestGameSession.Metadata(40100001, type: 2, slotMax: 1000);
            ItemTag tag = skinType != 0 ? ItemTag.SkinCrystal : ItemTag.CrystalPiece;
            chips = test.Store(chipData with { Property = chipData.Property with { Tag = tag } }, amount: chipCount);
        }
        test.Register(TestGameSession.Metadata(40200001, type: 13));
        GameSession session = test.Start();
        test.TakeSent();
        session.Currency.Meso = mesos;
        gear = session.Item.Inventory.Get(gear.Uid)!;
        gear.Socket = new ItemSocket(3, 1);
        materials = materials.Select(material => session.Item.Inventory.Get(material.Uid)!).ToArray();
        foreach (Item material in materials) {
            material.Socket = new ItemSocket(3, 1);
        }
        return session;
    }

    private void Unlock(GameSession session, params long[] uids) {
        new ItemSocketHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(gear.Uid);
            writer.WriteByte((byte) uids.Length);
            foreach (long uid in uids) {
                writer.WriteLong(uid);
            }
        }));
    }

    private int Chips(GameSession session) => chips == null ? 0 : session.Item.Inventory.Get(chips.Uid)?.Amount ?? 0;

    private int MaterialsLeft(GameSession session) => materials.Count(material => session.Item.Inventory.Get(material.Uid) != null);

    private string[] Sent() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString).ToArray();
    }

    private static string Error(Maple2.Model.Error.ItemSocketError error) => Convert.ToHexString(ItemSocketPacket.Error(error: error).ToArray());

    [Test]
    public void FragmentsShort_ArePaidInMesos() {
        GameSession session = Start(chipCount: 150, mesos: 60000);

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(2));
            Assert.That(Chips(session), Is.Zero);
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(MaterialsLeft(session), Is.Zero);
        });
    }

    [Test]
    public void MesosShort_TakeNothing() {
        GameSession session = Start(chipCount: 150, mesos: 49999);

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(Chips(session), Is.EqualTo(150));
            Assert.That(session.Currency.Meso, Is.EqualTo(49999));
            Assert.That(MaterialsLeft(session), Is.EqualTo(2));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_lack_price) }));
        });
    }

    [Test]
    public void OneMaterial_IsRefused() {
        GameSession session = Start(chipCount: 200, mesos: 0, materialCount: 1);

        Unlock(session, materials[0].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(Chips(session), Is.EqualTo(200));
            Assert.That(MaterialsLeft(session), Is.EqualTo(1));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_invalid_target_ingredient_count) }));
        });
    }

    // With ItemSocketExpansion100 off the page asks one copy (FUN_140628350 gives 1): one unlocks, two are refused.
    [Test]
    public void ExpansionOff_OneMaterialUnlocks() {
        GameSession session = Start(chipCount: 200, mesos: 0, materialCount: 1);
        test.SetFeaturesOff("ItemSocketExpansion100");

        Unlock(session, materials[0].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(2));
            Assert.That(Chips(session), Is.Zero);
            Assert.That(MaterialsLeft(session), Is.Zero);
        });
    }

    [Test]
    public void ExpansionOff_TwoMaterials_IsRefused() {
        GameSession session = Start(chipCount: 200, mesos: 0);
        test.SetFeaturesOff("ItemSocketExpansion100");

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(Chips(session), Is.EqualTo(200));
            Assert.That(MaterialsLeft(session), Is.EqualTo(2));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_invalid_target_ingredient_count) }));
        });
    }

    [Test]
    public void SameMaterialTwice_TakesNothing() {
        GameSession session = Start(chipCount: 200, mesos: 0);

        Unlock(session, materials[0].Uid, materials[0].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(Chips(session), Is.EqualTo(200));
            Assert.That(MaterialsLeft(session), Is.EqualTo(2));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_invalid_target_ingredient) }));
        });
    }

    [Test]
    public void MaterialWithGem_IsTakenWithItsGem() {
        GameSession session = Start(chipCount: 200, mesos: 0);
        materials[0].Socket!.Sockets[0] = new ItemGemstone(40200001, stats: new ItemStats());

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(2));
            Assert.That(MaterialsLeft(session), Is.Zero);
            Assert.That(session.Item.Inventory.Find(40200001), Is.Empty);
        });
    }

    [Test]
    public void MaterialWithLockedGem_IsRefused() {
        GameSession session = Start(chipCount: 200, mesos: 0);
        materials[0].Socket!.Sockets[0] = new ItemGemstone(40200001, stats: new ItemStats(), isLocked: true);

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(Chips(session), Is.EqualTo(200));
            Assert.That(MaterialsLeft(session), Is.EqualTo(2));
        });
    }

    // A locked copy, or one still unlocking, is refused before anything is taken.
    [TestCase(true, 0L)]
    [TestCase(false, 1L)]
    public void LockedMaterial_IsRefused(bool locked, long unlockIn) {
        GameSession session = Start(chipCount: 200, mesos: 0);
        materials[0].IsLocked = locked;
        materials[0].UnlockTime = unlockIn == 0 ? 0 : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(Chips(session), Is.EqualTo(200));
            Assert.That(MaterialsLeft(session), Is.EqualTo(2));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_invalid_target_ingredient) }));
        });
    }

    // An expired copy is refused before anything is taken: its consume would fail after the price.
    [Test]
    public void ExpiredMaterial_IsRefused() {
        GameSession session = Start(chipCount: 200, mesos: 0);
        materials[0].ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(1));
            Assert.That(Chips(session), Is.EqualTo(200));
            Assert.That(Sent(), Is.EqualTo(new[] { Error(s_itemsocketsystem_error_invalid_target_ingredient) }));
        });
    }

    // An outfit's unlock takes no copy and 690 skin crystals, and binds the outfit to the character.
    [Test]
    public void SkinUnlock_BindsTheItem() {
        GameSession session = Start(chipCount: 690, mesos: 0, materialCount: 0, skinType: 1);
        Assert.That(gear.Transfer, Is.Not.Null);
        Assert.That(gear.Transfer!.Binding, Is.Null);

        Unlock(session);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(2));
            Assert.That(Chips(session), Is.Zero);
            Assert.That(gear.Transfer!.Binding?.CharacterId, Is.EqualTo(session.CharacterId));
        });
    }

    [Test]
    public void FeeAttribute_LowersTheFragments() {
        GameSession session = Start(chipCount: 200, mesos: 0);
        session.Player.Stats.Values[SpecialAttribute.ReduceGameItemSocketUnlockFee].AddTotal(100);

        Unlock(session, materials[0].Uid, materials[1].Uid);

        Assert.Multiple(() => {
            Assert.That(gear.Socket!.UnlockSlots, Is.EqualTo(2));
            Assert.That(Chips(session), Is.EqualTo(20));
        });
    }
}

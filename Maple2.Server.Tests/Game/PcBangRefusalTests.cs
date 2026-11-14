using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A PC bang item (<PCBang PCBang="1"/>) away from a PC bang is refused for a ride (MyPC's ride start via CanUseItem,
// notice 0xe2, mypc.cpp:4656), a pet summon or switch (MyPC::FUN_14152b2d0, mypc.cpp:3252) and a fishing rod
// (CFishingManager::CanUseBait, fishingmanager.cpp:1040). The client asks these itself; a forged request is refused.
public class PcBangRefusalTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private static ItemMetadata PcBang(ItemMetadata metadata) => metadata with { Limit = metadata.Limit with { RequirePcBang = true } };

    // A field that allows rides and pets.
    private void Start() {
        test.SetServerTable(PcBangTests.Kr);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true),
        });
        test.TakeSent();
    }

    // At a PC bang: the grade alone (SetGrade's buffs need skill data the field does not have).
    private void AtPcBang() => typeof(PcBangManager).GetField("<Grade>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(session.PcBang, 1);

    [Test]
    public void Ride() {
        test = new TestGameSession();
        ItemMetadata mount = TestGameSession.Metadata(50600001, type: 5, slotMax: 1);
        mount = PcBang(mount with { Property = mount.Property with { Ride = 21000021 } });
        Item item = test.Store(mount);
        Start();
        session.Ride = new RideManager(session);

        new RideHandler { RideMetadata = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0); // Start
            writer.WriteByte((byte) RideOnType.Default);
            writer.WriteInt(21000021);
            writer.WriteInt(0); // object id
            writer.WriteBool(false);
            writer.WriteInt(0);
            writer.WriteLong(item.Uid);
        }));

        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Notice).ToArray(),
            Has.Length.EqualTo(1));
        Assert.That(session.PcBang.Refuses(session.Item.Inventory.Get(item.Uid)!), Is.True);
        AtPcBang();
        Assert.That(session.PcBang.Refuses(session.Item.Inventory.Get(item.Uid)!), Is.False);
    }

    [Test]
    public void PetSummonAndSwitch() {
        test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(60000001, type: 11, slotMax: 1);
        petData = PcBang(petData with { Property = petData.Property with { PetId = 60000001 } });
        Item pet = test.Store(petData);
        Start();

        PetHandler.SummonPet(session, pet.Uid);
        Assert.That(session.Pet, Is.Null);
        Assert.That(PetHandler.CanSwitchTo(session, pet.Uid), Is.False);

        AtPcBang();
        Assert.That(PetHandler.CanSwitchTo(session, pet.Uid), Is.True);
    }

    [Test]
    public void FishingRod() {
        test = new TestGameSession();
        ItemMetadata rodData = TestGameSession.Metadata(32000001, type: 2, slotMax: 1,
            function: new ItemMetadataFunction(ItemFunction.FishingRod, "FishingRod", "1", false));
        test.Register(rodData);
        test.SetTable(new FishingRodTable(new Dictionary<int, FishingRodTable.Entry> { [1] = new(32000001, 100, 0, 0) }));
        Item rod = test.Store(PcBang(rodData));
        Start();
        session.Fishing = new FishingManager(session, test.TableMetadata, test.ServerTableMetadata);
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Field, new FieldAccelerationStructure());

        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.s_fishing_error_invalid_item));
        Assert.That(session.GuideObject, Is.Null);

        // At a PC bang the rod passes to the next check, the rod's mastery (the character has none).
        AtPcBang();
        Assert.That(session.Fishing.Prepare(rod.Uid), Is.EqualTo(FishingError.s_fishing_error_fishingrod_mastery));
    }
}

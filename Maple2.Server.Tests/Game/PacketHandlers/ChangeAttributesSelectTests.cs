using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A change of attributes kept (ChangeAttributes 2) on worn gear recounts the owner's stats, as the client recounts the
// equipment's (UIItemRemakeOptionDialog FUN_1412a1650, unit_14129cfa0_itemremakeoptiondialog.cpp:877-894). A bag item
// does not count in the totals, so it needs none.
public class ChangeAttributesSelectTests {
    [Test]
    public void WornGear_CountsInStats_BagGearDoesNot() {
        var test = new TestGameSession();
        Item worn = test.Store(TestGameSession.Metadata(11200001, slot: EquipSlot.RH, type: 1, slotMax: 1), group: ItemGroup.Gear, slot: EquipSlot.RH);
        Item bag = test.Store(TestGameSession.Metadata(11200002, slot: EquipSlot.RH, type: 1, slotMax: 1));
        GameSession session = test.Start();
        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Gear.Values.Select(item => item.Uid), Does.Contain(worn.Uid));
            Assert.That(ChangeAttributesHandler.CountsInStats(session, worn.Uid), Is.True);
            Assert.That(ChangeAttributesHandler.CountsInStats(session, bag.Uid), Is.False);
        });
    }

    // A remake scroll's result kept (ChangeAttributesScroll Select) on the summoned pet puts the options on the pet
    // and asks for the recount, as the dialog path does; a pet left in the bag needs none. Before, the scroll path
    // never recounted.
    [Test]
    public void ScrollSelect_OnTheSummonedPet_Recounts() {
        var test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(60000001, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = 60000001 } };
        Item pet = test.Store(petData, group: ItemGroup.Default);
        Item other = test.Store(petData);
        GameSession session = test.Start();
        Item summoned = session.Item.Inventory.Get(pet.Uid)!;
        Item bagPet = session.Item.Inventory.Get(other.Uid)!;
        summoned.Stats = new ItemStats();
        bagPet.Stats = new ItemStats();
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var fieldPet = (FieldPet) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), any)!.SetValue(fieldPet, summoned);
        var manager = (PetManager) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", any)!.SetValue(manager, fieldPet);
        session.Pet = manager;

        var result = new ItemStats();
        result[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(7);
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesScrollHandler.Keep(session, summoned, result), Is.True);
            Assert.That(summoned.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(7));
            Assert.That(ChangeAttributesScrollHandler.Keep(session, bagPet, result), Is.False);
        });
    }
}

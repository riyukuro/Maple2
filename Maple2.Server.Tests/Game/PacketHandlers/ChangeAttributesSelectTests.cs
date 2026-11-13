using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
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
}

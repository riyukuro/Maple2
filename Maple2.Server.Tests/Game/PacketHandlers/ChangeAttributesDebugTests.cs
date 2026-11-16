using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The pet remake tab's GM button (OnDebug, TabContentsPetRemakeOption::SendChangeAttributesMode7,
// tabcontentspetremakeoption.cpp:1009-1033, GM level 50 or more) sends ChangeAttributes 7 with RemakeOptionDebug
// (itemremakescrolldata.cpp:469, 494, 534): the pet uid, a second uid, a lock count with a flag byte and a short per
// lock, a kind byte and a value. The server reads it all for a GM and drops it for anyone else; it changes nothing on
// the pet and answers nothing either way.
public class ChangeAttributesDebugTests {
    private TestGameSession test = null!;
    private Item pet = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(60000001, type: 11, slotMax: 1);
        petData = petData with { Property = petData.Property with { PetId = 60000001 } };
        long uid = test.Store(petData).Uid;
        test.Start();
        pet = test.Session.Item.Inventory.Get(uid)!;
        pet.Stats = new ItemStats();
        pet.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(7);
        test.TakeSent();
    }

    private ByteReader Debug() {
        ByteReader packet = TestGameSession.Packet(writer => {
            writer.WriteByte(7);
            writer.WriteLong(pet.Uid);
            writer.WriteLong(0);
            writer.WriteByte(1);
            writer.WriteBool(false);
            writer.WriteShort(1);
            writer.WriteByte(2);
            writer.WriteInt(100);
        });
        new ChangeAttributesHandler { ItemStatsCalc = test.ItemStats }.Handle(test.Session, packet);
        return packet;
    }

    [TestCase(AdminPermissions.GameMaster)]
    [TestCase(AdminPermissions.None)]
    public void Debug_ChangesNothingAndAnswersNothing(AdminPermissions permissions) {
        test.Session.Player.AdminPermissions = permissions;
        ByteReader packet = Debug();
        Assert.Multiple(() => {
            if (permissions != AdminPermissions.None) {
                Assert.That(packet.Available, Is.EqualTo(0), "the GM's packet is read to its end");
            }
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(pet.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(7));
            Assert.That(pet.Stats[ItemStats.Type.Random].Count, Is.EqualTo(1));
        });
    }
}

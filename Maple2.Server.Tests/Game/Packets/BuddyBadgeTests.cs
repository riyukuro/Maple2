using System;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

public class BuddyBadgeTests {
    private static byte[] Body(ByteWriter writer) {
        return writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
    }

    [Test]
    public void ShowToNewcomer_IsKind2AndThePlayer() {
        Assert.That(Body(BuddyBadgePacket.ShowToNewcomer(0x0102030405060708)),
            Is.EqualTo(new byte[] { 2, 8, 7, 6, 5, 4, 3, 2, 1 }));
    }

    [Test]
    public void PartnerName_IsKind3PlayerBadgeUidAndName() {
        Assert.That(Body(BuddyBadgePacket.PartnerName(5, 9, "ab")), Is.EqualTo(new byte[] {
            3,
            5, 0, 0, 0, 0, 0, 0, 0,
            9, 0, 0, 0, 0, 0, 0, 0,
            2, 0, (byte) 'a', 0, (byte) 'b', 0,
        }));
    }

    [Test]
    public void Renamed_OnlyForThisPartnerAndANewName() {
        var info = new ItemCoupleInfo(7, "old", true);

        ItemCoupleInfo? renamed = BuddyBadgeRule.Renamed(info, 7, "new");
        Assert.That(renamed, Is.Not.Null);
        Assert.That(renamed!.CharacterId, Is.EqualTo(7));
        Assert.That(renamed.Name, Is.EqualTo("new"));
        Assert.That(renamed.IsCreator, Is.True);

        Assert.That(BuddyBadgeRule.Renamed(info, 7, "old"), Is.Null);
        Assert.That(BuddyBadgeRule.Renamed(info, 8, "new"), Is.Null);
        Assert.That(BuddyBadgeRule.Renamed(info, 7, ""), Is.Null);
        Assert.That(BuddyBadgeRule.Renamed(new ItemCoupleInfo(), 0, "new"), Is.Null);
    }
}

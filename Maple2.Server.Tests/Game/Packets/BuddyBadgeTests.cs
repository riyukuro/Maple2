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

    // Modes 0 and 1: i64 character id, and only the sender's own id is passed on.
    [Test]
    public void Subject_IsOneLongAndTheSendersOwn() {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        var reader = new ByteReader(writer.ToArray(), 0);
        long subject = BuddyBadgeRule.ReadSubject(reader);
        Assert.That(subject, Is.EqualTo(0x1122334455667788));
        Assert.That(reader.Available, Is.EqualTo(0));

        Assert.That(BuddyBadgeRule.OwnSubject(subject, 0x1122334455667788), Is.True);
        Assert.That(BuddyBadgeRule.OwnSubject(subject, 5), Is.False);
        Assert.That(BuddyBadgeRule.OwnSubject(0, 0), Is.False);
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

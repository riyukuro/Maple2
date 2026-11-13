using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Buddy sub-type 13 as CBuddyManager::OnBuddy reads it: an int count, then per notice the other player's name, the
// action string and a long id.
public class BuddyNoticePacketTests {
    [Test]
    public void Notices_Layout() {
        byte[] bytes = BuddyPacket.Notices(new List<BuddyNotice> {
            new("Ab", BuddyNoticeType.Approved, 0x0102),
            new("C", BuddyNoticeType.Removed, 7),
        }).ToArray();

        string expected = Convert.ToHexString(BitConverter.GetBytes((short) SendOp.Buddy))
                          + "0D" + "02000000"
                          + "0200" + "41006200" + "0800" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("approved"))
                          + "0201000000000000"
                          + "0100" + "4300" + "0700" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("removed"))
                          + "0700000000000000";
        Assert.That(Convert.ToHexString(bytes), Is.EqualTo(expected));
    }

    [TestCase(BuddyNoticeType.Approved, "approved")]
    [TestCase(BuddyNoticeType.Declined, "declined")]
    [TestCase(BuddyNoticeType.Removed, "removed")]
    public void NoticeAction_IsTheClientString(BuddyNoticeType type, string action) {
        Assert.That(BuddyPacket.NoticeAction(type), Is.EqualTo(action));
    }
}

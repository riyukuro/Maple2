using System;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Network;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// BadgeEquip 3: the transparency badge's settings (UIGemSettingDialog OnOk, uigemsettingdialog.cpp:154): byte 3,
// byte badge slot, then the ten hidden flags as bytes (FUN_1401fd2e0). Made-up badge ids (701xxxx1 is a
// transparency badge, 704xxxxx a damage skin).
public class BadgeTransparencyTests {
    private const int TransparencyBadge = 70100001;
    private const int DamageBadge = 70400001;

    private TestGameSession test = null!;

    private GameSession Start(int badgeId, BadgeType slot) {
        test = new TestGameSession();
        Item badge = test.Store(TestGameSession.Metadata(badgeId, type: 7));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        Assert.That(session.Item.Inventory.Add(badge), Is.True);
        new BadgeEquipHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(badge.Uid);
            writer.WriteByte((byte) slot);
        }));
        Assume.That(session.Item.Equips.Get(slot)?.Uid, Is.EqualTo(badge.Uid));
        test.TakeSent();
        return session;
    }

    private static void Set(GameSession session, BadgeType slot, bool[] hidden) {
        new BadgeEquipHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteByte((byte) slot);
            foreach (bool flag in hidden) {
                writer.WriteBool(flag);
            }
        }));
    }

    private static bool Closing(GameSession session) {
        return (int) typeof(Session).GetField("disconnecting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)! == 1;
    }

    // The worn transparency badge keeps the ten flags and the field is sent the badge again.
    [Test]
    public void Settings_AreStoredOnTheWornBadge() {
        GameSession session = Start(TransparencyBadge, BadgeType.Transparency);
        bool[] hidden = [true, false, true, false, false, false, false, false, true, true];

        Set(session, BadgeType.Transparency, hidden);

        Item worn = session.Item.Equips.Get(BadgeType.Transparency)!;
        Assert.Multiple(() => {
            Assert.That(worn.Badge!.Transparency, Is.EqualTo(hidden));
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Does.Contain(Convert.ToHexString(EquipPacket.EquipBadge(session.Player, worn).ToArray())));
            Assert.That(Closing(session), Is.False);
        });
    }

    // The client sends it only with a transparency badge worn (GetTransparencyGem): a slot holding another badge
    // cuts the session and changes nothing.
    [Test]
    public void OtherBadge_Cut() {
        GameSession session = Start(DamageBadge, BadgeType.Damage);

        Set(session, BadgeType.Damage, Enumerable.Repeat(true, 10).ToArray());

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(BadgeType.Damage)!.Badge!.Transparency, Is.All.False);
            Assert.That(Closing(session), Is.True);
        });
    }
}

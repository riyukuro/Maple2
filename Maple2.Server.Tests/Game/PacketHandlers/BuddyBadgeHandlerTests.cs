using System;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// BuddyBadge (CNetworkService::OnBuddyBadge): the couple effect of two players wearing the matching buddy badges.
// 0 turns the sender's effect on for the field, 1 off, 2 shows it to a newcomer alone, 3 sends the partner's current
// name for the sender's badge. The sender may only speak for itself, the badges have to be a matching pair, and the
// partner has to be on the field. The ids are made up.
public class BuddyBadgeHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int BadgeId = 70000001;
    private const long BadgeUid = 4242;

    private TestGameSession a = null!;
    private TestGameSession b = null!;
    private TestGameSession c = null!;

    private static void Join(FieldManager field, TestGameSession session, int objectId) {
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(session.Session.Player, objectId);
        field.Players[objectId] = session.Session.Player;
        session.Session.Field = field;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(session.Session.Player, field);
    }

    private static void Wear(TestGameSession wearer, int badgeId, long partnerId, string partnerName, long uid = BadgeUid) {
        var badge = new Item(TestGameSession.Metadata(badgeId)) {
            Uid = uid,
            CoupleInfo = new ItemCoupleInfo(partnerId, partnerName, true),
        };
        wearer.Session.Item.Equips.Badge[BadgeType.Buddy] = badge;
    }

    // a and b are partners wearing the same badge; c stands in the field wearing nothing.
    [SetUp]
    public void SetUp() {
        a = new TestGameSession();
        a.Start();
        b = new TestGameSession(a.Options);
        b.Start();
        c = new TestGameSession(a.Options);
        c.Start();
        FieldManager field = a.Session.Field!;
        Join(field, b, 2);
        Join(field, c, 3);
        Wear(a, BadgeId, b.Session.CharacterId, "old");
        Wear(b, BadgeId, a.Session.CharacterId, "partner");
        foreach (TestGameSession test in new[] { a, b, c }) {
            test.TakeSent();
        }
    }

    private static void Send(TestGameSession sender, Action<Maple2.PacketLib.Tools.ByteWriter> write) {
        new BuddyBadgeHandler().Handle(sender.Session, TestGameSession.Packet(write));
    }

    private void Start(long subject) => Send(a, writer => {
        writer.WriteByte(0);
        writer.WriteLong(subject);
    });

    private static byte[][] Badges(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.BuddyBadge).ToArray();

    [Test]
    public void Start_MatchedPair_TellsTheFieldButNotTheSender() {
        Start(a.Session.CharacterId);

        Assert.Multiple(() => {
            Assert.That(Badges(a), Is.Empty);
            Assert.That(Badges(b), Is.EqualTo(new[] { BuddyBadgePacket.Start(a.Session.CharacterId).ToArray() }));
            Assert.That(Badges(c), Is.EqualTo(new[] { BuddyBadgePacket.Start(a.Session.CharacterId).ToArray() }));
        });
    }

    [Test]
    public void Start_ForAnotherPlayer_IsDropped() {
        Start(b.Session.CharacterId);

        Assert.Multiple(() => {
            Assert.That(Badges(b), Is.Empty);
            Assert.That(Badges(c), Is.Empty);
        });
    }

    [Test]
    public void Start_PartnerWearsAnotherBadge_IsDropped() {
        Wear(b, BadgeId + 1, a.Session.CharacterId, "partner");

        Start(a.Session.CharacterId);

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void Start_PartnerBadgeNamesSomeoneElse_IsDropped() {
        Wear(b, BadgeId, c.Session.CharacterId, "partner");

        Start(a.Session.CharacterId);

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void Start_PartnerWearsNoBadge_IsDropped() {
        b.Session.Item.Equips.Badge.Clear();

        Start(a.Session.CharacterId);

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void Start_PartnerNotInTheField_IsDropped() {
        a.Session.Field!.Players.TryRemove(2, out _);

        Start(a.Session.CharacterId);

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void Start_NoBadgeWorn_IsDropped() {
        a.Session.Item.Equips.Badge.Clear();

        Start(a.Session.CharacterId);

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void Start_BadgeWithoutPartner_IsDropped() {
        a.Session.Item.Equips.Badge[BadgeType.Buddy] = new Item(TestGameSession.Metadata(BadgeId));

        Start(a.Session.CharacterId);

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void Stop_OwnSubject_TellsTheFieldButNotTheSender() {
        Send(a, writer => {
            writer.WriteByte(1);
            writer.WriteLong(a.Session.CharacterId);
        });

        Assert.Multiple(() => {
            Assert.That(Badges(a), Is.Empty);
            Assert.That(Badges(b), Is.EqualTo(new[] { BuddyBadgePacket.Stop(a.Session.CharacterId).ToArray() }));
            Assert.That(Badges(c), Is.EqualTo(new[] { BuddyBadgePacket.Stop(a.Session.CharacterId).ToArray() }));
        });
    }

    [TestCase(0L)]
    [TestCase(99999L)]
    public void Stop_OtherOrZeroSubject_IsDropped(long subject) {
        Send(a, writer => {
            writer.WriteByte(1);
            writer.WriteLong(subject == 0 ? 0 : b.Session.CharacterId);
        });

        Assert.Multiple(() => {
            Assert.That(Badges(b), Is.Empty);
            Assert.That(Badges(c), Is.Empty);
        });
    }

    [Test]
    public void ShowToNewcomer_TellsTheNewcomerOnly() {
        Send(a, writer => {
            writer.WriteByte(2);
            writer.WriteLong(a.Session.CharacterId);
            writer.WriteLong(c.Session.CharacterId);
        });

        Assert.Multiple(() => {
            Assert.That(Badges(c), Is.EqualTo(new[] { BuddyBadgePacket.ShowToNewcomer(a.Session.CharacterId).ToArray() }));
            Assert.That(Badges(a), Is.Empty);
            Assert.That(Badges(b), Is.Empty);
        });
    }

    [Test]
    public void ShowToNewcomer_NewcomerIsTheSender_IsDropped() {
        Send(a, writer => {
            writer.WriteByte(2);
            writer.WriteLong(a.Session.CharacterId);
            writer.WriteLong(a.Session.CharacterId);
        });

        Assert.That(Badges(a), Is.Empty);
    }

    [Test]
    public void ShowToNewcomer_ForAnotherPlayersEffect_IsDropped() {
        Send(a, writer => {
            writer.WriteByte(2);
            writer.WriteLong(b.Session.CharacterId);
            writer.WriteLong(c.Session.CharacterId);
        });

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void ShowToNewcomer_UnknownNewcomer_IsDropped() {
        Send(a, writer => {
            writer.WriteByte(2);
            writer.WriteLong(a.Session.CharacterId);
            writer.WriteLong(99999);
        });

        Assert.Multiple(() => {
            Assert.That(Badges(b), Is.Empty);
            Assert.That(Badges(c), Is.Empty);
        });
    }

    [Test]
    public void ShowToNewcomer_PartnerWithoutTheBadge_IsDropped() {
        b.Session.Item.Equips.Badge.Clear();

        Send(a, writer => {
            writer.WriteByte(2);
            writer.WriteLong(a.Session.CharacterId);
            writer.WriteLong(c.Session.CharacterId);
        });

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void PartnerName_Renamed_UpdatesTheBadgeAndTellsTheField() {
        b.Session.Player.Value.Character.Name = "fresh";

        Send(a, writer => {
            writer.WriteByte(3);
            writer.WriteLong(b.Session.CharacterId);
        });

        Assert.Multiple(() => {
            Assert.That(a.Session.Item.Equips.Badge[BadgeType.Buddy].CoupleInfo!.Name, Is.EqualTo("fresh"));
            Assert.That(a.Session.Item.Equips.Badge[BadgeType.Buddy].CoupleInfo!.IsCreator, Is.True);
            byte[] expected = BuddyBadgePacket.PartnerName(a.Session.CharacterId, BadgeUid, "fresh").ToArray();
            // The field is told, the sender too (no sender is left out of this broadcast).
            Assert.That(Badges(a), Is.EqualTo(new[] { expected }));
            Assert.That(Badges(b), Is.EqualTo(new[] { expected }));
            Assert.That(Badges(c), Is.EqualTo(new[] { expected }));
        });
    }

    [Test]
    public void PartnerName_SameName_SendsNothing() {
        b.Session.Player.Value.Character.Name = "old";

        Send(a, writer => {
            writer.WriteByte(3);
            writer.WriteLong(b.Session.CharacterId);
        });

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void PartnerName_ForSomeoneElseThanThePartner_IsDropped() {
        c.Session.Player.Value.Character.Name = "stranger";

        Send(a, writer => {
            writer.WriteByte(3);
            writer.WriteLong(c.Session.CharacterId);
        });

        Assert.Multiple(() => {
            Assert.That(a.Session.Item.Equips.Badge[BadgeType.Buddy].CoupleInfo!.Name, Is.EqualTo("old"));
            Assert.That(Badges(c), Is.Empty);
        });
    }

    [Test]
    public void PartnerName_PartnerNotInTheField_IsDropped() {
        a.Session.Field!.Players.TryRemove(2, out _);
        b.Session.Player.Value.Character.Name = "fresh";

        Send(a, writer => {
            writer.WriteByte(3);
            writer.WriteLong(b.Session.CharacterId);
        });

        Assert.That(a.Session.Item.Equips.Badge[BadgeType.Buddy].CoupleInfo!.Name, Is.EqualTo("old"));
    }

    [Test]
    public void PartnerName_NoBadge_IsDropped() {
        a.Session.Item.Equips.Badge.Clear();
        b.Session.Player.Value.Character.Name = "fresh";

        Send(a, writer => {
            writer.WriteByte(3);
            writer.WriteLong(b.Session.CharacterId);
        });

        Assert.That(Badges(c), Is.Empty);
    }

    [Test]
    public void UnknownCommand_IsIgnored() {
        Assert.DoesNotThrow(() => Send(a, writer => writer.WriteByte(9)));
        Assert.That(Badges(c), Is.Empty);
    }
}

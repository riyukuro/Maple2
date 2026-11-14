using System.Numerics;
using System.Reflection;
using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// BuddyEmote: the client asks for and accepts a couple emote only while the two players are within
// CoupleEmotionMaxDistance (300) of each other, and the start point it sends with an accept is its own position
// moved by the emote's node offsets (statecoupleemotion.cpp:869-881, 1207, 1357). The server sent the invite to a
// player anywhere in the field and passed on any start point, which moves the inviter there.
public class BuddyEmoteReachTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Emote = 90200011;

    private sealed class Scene {
        public required TestGameSession Inviter;
        public required TestGameSession Invited;
    }

    // The inviter at the origin, the invited player at (x, 0, 0), both in the inviter's field.
    private static Scene Start(float x) {
        var a = new TestGameSession();
        a.Start();
        var b = new TestGameSession(a.Options);
        b.Start();
        FieldManager field = a.Session.Field!;
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(b.Session.Player, field);
        a.Session.Player.Position = Vector3.Zero;
        b.Session.Player.Position = new Vector3(x, 0, 0);
        a.Session.Player.Value.Unlock.Emotes.Add(Emote);
        // The test session is made without its field initializers.
        foreach (TestGameSession test in new[] { a, b }) {
            typeof(Maple2.Server.Game.Session.GameSession).GetField(nameof(test.Session.BuddyEmoteInvites), Any)!.SetValue(test.Session, new BuddyEmoteInvites());
        }
        a.TakeSent();
        b.TakeSent();
        return new Scene { Inviter = a, Invited = b };
    }

    private static void Invite(Scene scene) {
        new BuddyEmoteHandler().Handle(scene.Inviter.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteInt(Emote);
            writer.WriteLong(scene.Invited.Session.CharacterId);
        }));
    }

    private static void Accept(Scene scene, Vector3 start) {
        new BuddyEmoteHandler().Handle(scene.Invited.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(Emote);
            writer.WriteLong(scene.Inviter.Session.CharacterId);
            writer.WriteFloat(start.X);
            writer.WriteFloat(start.Y);
            writer.WriteFloat(start.Z);
            writer.WriteFloat(start.X);
            writer.WriteFloat(start.Y);
            writer.WriteFloat(start.Z);
            writer.WriteFloat(90);
        }));
    }

    private static byte[] TooFar => BuddyEmotePacket.Error(BuddyEmoteError.s_couple_emotion_failed_teleport_limit_distance).ToArray();

    [Test]
    public void AnInviteNearby_IsPassedOn() {
        Scene scene = Start(200);
        Invite(scene);
        Assert.Multiple(() => {
            Assert.That(scene.Invited.TakeSent(), Has.Count.EqualTo(1));
            Assert.That(scene.Invited.Session.BuddyEmoteInvites.Has(scene.Inviter.Session.CharacterId), Is.True);
        });
    }

    [Test]
    public void AnInviteFromAcrossTheField_IsRefused() {
        Scene scene = Start(5000);
        Invite(scene);
        Assert.Multiple(() => {
            Assert.That(scene.Invited.TakeSent(), Is.Empty);
            Assert.That(scene.Invited.Session.BuddyEmoteInvites.Has(scene.Inviter.Session.CharacterId), Is.False);
            Assert.That(scene.Inviter.TakeSent(), Is.EqualTo(new[] { TooFar }));
        });
    }

    [Test]
    public void AnAcceptAtTheAccepter_Starts() {
        Scene scene = Start(200);
        Invite(scene);
        scene.Invited.TakeSent();
        Accept(scene, new Vector3(250, 0, 0));
        Assert.Multiple(() => {
            Assert.That(scene.Inviter.TakeSent(), Has.Count.EqualTo(2));
            Assert.That(scene.Invited.TakeSent(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void AnAcceptWithAFarStartPoint_IsRefused() {
        Scene scene = Start(200);
        Invite(scene);
        scene.Invited.TakeSent();
        Accept(scene, new Vector3(8000, 0, 0));
        Assert.Multiple(() => {
            Assert.That(scene.Inviter.TakeSent(), Is.EqualTo(new[] { TooFar }));
            Assert.That(scene.Invited.TakeSent(), Is.EqualTo(new[] { TooFar }));
            Assert.That(scene.Invited.Session.BuddyEmoteInvites.Has(scene.Inviter.Session.CharacterId), Is.False);
        });
    }

    [Test]
    public void AnAcceptAfterTheInviterWalkedAway_IsRefused() {
        Scene scene = Start(200);
        Invite(scene);
        scene.Invited.TakeSent();
        scene.Inviter.Session.Player.Position = new Vector3(-5000, 0, 0);
        Accept(scene, new Vector3(250, 0, 0));
        Assert.Multiple(() => {
            Assert.That(scene.Inviter.TakeSent(), Is.EqualTo(new[] { TooFar }));
            Assert.That(scene.Invited.TakeSent(), Is.EqualTo(new[] { TooFar }));
        });
    }

    [Test]
    public void Reach_IsTheClientConstantPlusSlack() {
        Assert.Multiple(() => {
            Assert.That(BuddyEmoteRules.InReach(Vector3.Zero, new Vector3(BuddyEmoteRules.MaxDistance + InteractRules.ReachSlack, 0, 0)), Is.True);
            Assert.That(BuddyEmoteRules.InReach(Vector3.Zero, new Vector3(BuddyEmoteRules.MaxDistance + InteractRules.ReachSlack + 1, 0, 0)), Is.False);
        });
    }
}

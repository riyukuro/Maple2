using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// BuddyEmote through the handler (networkservice.cpp:1104-1190): mode 0 invites, 1 confirms the box to the inviter,
// 2 passes an error reason back, 3 accepts and starts the emote for both, 4 declines, 6 cancels. Answers (1-4) are
// passed on only for an open invite of that player, and an invite ends with its answer. The ids are made up.
public class BuddyEmoteHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Emote = 90200011;

    private TestGameSession a = null!; // inviter
    private TestGameSession b = null!; // invited
    private long aId => a.Session.CharacterId;
    private long bId => b.Session.CharacterId;

    [SetUp]
    public void SetUp() {
        a = new TestGameSession();
        a.Start();
        b = new TestGameSession(a.Options);
        b.Start();
        FieldManager field = a.Session.Field!;
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(b.Session.Player, field);
        a.Session.Player.Position = Vector3.Zero;
        b.Session.Player.Position = new Vector3(100, 0, 0);
        a.Session.Player.Value.Unlock.Emotes.Add(Emote);
        foreach (TestGameSession test in new[] { a, b }) {
            typeof(GameSession).GetField(nameof(test.Session.BuddyEmoteInvites), Any)!.SetValue(test.Session, new BuddyEmoteInvites());
        }
        a.TakeSent();
        b.TakeSent();
    }

    private static void Send(TestGameSession sender, Action<ByteWriter> write) =>
        new BuddyEmoteHandler().Handle(sender.Session, TestGameSession.Packet(write));

    private void Invite(int emote = Emote, long target = -1) => Send(a, writer => {
        writer.WriteByte(0);
        writer.WriteInt(emote);
        writer.WriteLong(target == -1 ? bId : target);
    });

    private void Accept(int emote = Emote) => Send(b, writer => {
        writer.WriteByte(3);
        writer.WriteInt(emote);
        writer.WriteLong(aId);
        for (int i = 0; i < 6; i++) {
            writer.WriteFloat(i == 0 ? 50 : 0);
        }
        writer.WriteFloat(90);
    });

    private static byte[] Err(BuddyEmoteError error) => BuddyEmotePacket.Error(error).ToArray();

    private static byte[][] Emotes(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.BuddyEmote).ToArray();

    [Test]
    public void Invite_PassesTheInviterNameAndEmote() {
        Invite();

        Assert.Multiple(() => {
            Assert.That(Emotes(b), Is.EqualTo(new[] { BuddyEmotePacket.Invite(Emote, a.Session.Player).ToArray() }));
            Assert.That(Emotes(a), Is.Empty);
        });
    }

    [Test]
    public void Invite_EmoteNotUnlocked_AnswersNoSkill() {
        a.Session.Player.Value.Unlock.Emotes.Remove(Emote);

        Invite();

        Assert.Multiple(() => {
            Assert.That(Emotes(a), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_request_not_exist_skill) }));
            Assert.That(Emotes(b), Is.Empty);
        });
    }

    [Test]
    public void Invite_TargetNotInTheField_AnswersWrongPosition() {
        Invite(target: 987654);

        Assert.That(Emotes(a), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_target_user_wrong_position) }));
    }

    [Test]
    public void Invite_Oneself_IsIgnored() {
        Invite(target: aId);

        Assert.Multiple(() => {
            Assert.That(Emotes(a), Is.Empty);
            Assert.That(a.Session.BuddyEmoteInvites.Has(aId), Is.False);
        });
    }

    [Test]
    public void InviteConfirm_OpenInvite_TellsTheInviter() {
        Invite();
        b.TakeSent();

        Send(b, writer => {
            writer.WriteByte(1);
            writer.WriteLong(aId);
        });

        Assert.Multiple(() => {
            Assert.That(Emotes(a), Is.EqualTo(new[] { BuddyEmotePacket.InviteConfirm(bId).ToArray() }));
            // The invite stays open after the confirm.
            Assert.That(b.Session.BuddyEmoteInvites.Has(aId), Is.True);
        });
    }

    [Test]
    public void InviteConfirm_NoInvite_IsDropped() {
        Send(b, writer => {
            writer.WriteByte(1);
            writer.WriteLong(aId);
        });

        Assert.That(Emotes(a), Is.Empty);
    }

    [Test]
    public void InviteConfirm_InviterGone_AnswersCannotFindUser() {
        Invite();
        b.TakeSent();
        a.Session.Field!.Players.TryRemove(1, out _);
        a.Session.Field.Players.TryRemove(a.Session.Player.ObjectId, out _);

        Send(b, writer => {
            writer.WriteByte(1);
            writer.WriteLong(aId);
        });

        Assert.That(Emotes(b), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_accept_cannot_find_request_user) }));
    }

    [Test]
    public void Error_OpenInvite_GoesBackToTheInviterAndEndsTheInvite() {
        Invite();
        b.TakeSent();

        Send(b, writer => {
            writer.WriteByte(2);
            writer.WriteLong(aId);
            writer.WriteByte((byte) BuddyEmoteError.s_couple_emotion_failed_teleport_limit_distance);
        });

        Assert.Multiple(() => {
            Assert.That(Emotes(a), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_teleport_limit_distance) }));
            Assert.That(b.Session.BuddyEmoteInvites.Has(aId), Is.False);
        });
    }

    [Test]
    public void Error_UnknownReason_IsDropped() {
        Invite();
        b.TakeSent();

        Send(b, writer => {
            writer.WriteByte(2);
            writer.WriteLong(aId);
            writer.WriteByte(250);
        });

        Assert.Multiple(() => {
            Assert.That(Emotes(a), Is.Empty);
            Assert.That(b.Session.BuddyEmoteInvites.Has(aId), Is.True);
        });
    }

    [Test]
    public void Error_NoInvite_IsDropped() {
        Send(b, writer => {
            writer.WriteByte(2);
            writer.WriteLong(aId);
            writer.WriteByte((byte) BuddyEmoteError.s_couple_emotion_failed_teleport_limit_distance);
        });

        Assert.That(Emotes(a), Is.Empty);
    }

    [Test]
    public void Accept_StartsTheEmoteForBothWithTheSameSeed() {
        Invite();
        b.TakeSent();

        Accept();

        byte[][] forA = Emotes(a);
        byte[][] forB = Emotes(b);
        Assert.Multiple(() => {
            Assert.That(forA, Has.Length.EqualTo(2));
            Assert.That(forA[0], Is.EqualTo(BuddyEmotePacket.Accept(Emote, bId).ToArray()));
            Assert.That(forB, Has.Length.EqualTo(1));
            Assert.That(forA[1], Is.EqualTo(forB[0]));
            Assert.That(forB[0][2], Is.EqualTo(5));
            var reader = new ByteReader(forB[0], 3);
            Assert.That(reader.ReadInt(), Is.EqualTo(Emote));
            Assert.That(reader.ReadLong(), Is.EqualTo(aId));
            Assert.That(reader.ReadLong(), Is.EqualTo(bId));
            Assert.That(reader.ReadFloat(), Is.EqualTo(50f));
        });
    }

    [Test]
    public void Accept_Twice_SecondIsRefused() {
        Invite();
        Accept();
        a.TakeSent();
        b.TakeSent();

        Accept();

        Assert.Multiple(() => {
            Assert.That(Emotes(b), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_accept_cannot_find_request_user) }));
            Assert.That(Emotes(a), Is.Empty);
        });
    }

    [Test]
    public void Accept_OtherEmoteThanInvited_IsRefusedAndKeepsTheInvite() {
        Invite();
        b.TakeSent();

        Accept(emote: Emote + 1);

        Assert.Multiple(() => {
            Assert.That(Emotes(b), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_accept_cannot_find_request_user) }));
            Assert.That(Emotes(a), Is.Empty);
            Assert.That(b.Session.BuddyEmoteInvites.Has(aId), Is.True);
        });
    }

    [Test]
    public void Accept_WithoutInvite_IsRefused() {
        Accept();

        Assert.That(Emotes(b), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_accept_cannot_find_request_user) }));
    }

    [Test]
    public void Accept_InviterLeftTheField_IsRefused() {
        Invite();
        b.TakeSent();
        a.Session.Field!.Players.TryRemove(a.Session.Player.ObjectId, out _);

        Accept();

        Assert.That(Emotes(b), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_accept_cannot_find_request_user) }));
    }

    [Test]
    public void Decline_OpenInvite_TellsTheInviterAndEndsIt() {
        Invite();
        b.TakeSent();

        Send(b, writer => {
            writer.WriteByte(4);
            writer.WriteInt(Emote);
            writer.WriteLong(aId);
        });

        Assert.Multiple(() => {
            Assert.That(Emotes(a), Is.EqualTo(new[] { BuddyEmotePacket.Decline(Emote, bId).ToArray() }));
            Assert.That(b.Session.BuddyEmoteInvites.Has(aId), Is.False);
        });
    }

    [Test]
    public void Decline_NoInvite_IsDropped() {
        Send(b, writer => {
            writer.WriteByte(4);
            writer.WriteInt(Emote);
            writer.WriteLong(aId);
        });

        Assert.That(Emotes(a), Is.Empty);
    }

    // The cancel names the player who cancelled, whoever it is sent to (the partner's client drops any other id).
    [Test]
    public void Cancel_TellsThePartnerAndEndsTheInvite() {
        Invite();
        b.TakeSent();

        Send(a, writer => {
            writer.WriteByte(6);
            writer.WriteInt(Emote);
            writer.WriteLong(bId);
        });

        Assert.Multiple(() => {
            Assert.That(Emotes(b), Is.EqualTo(new[] { BuddyEmotePacket.Cancel(Emote, aId).ToArray() }));
            Assert.That(b.Session.BuddyEmoteInvites.Has(aId), Is.False);
        });
        Accept();
        Assert.That(Emotes(b), Is.EqualTo(new[] { Err(BuddyEmoteError.s_couple_emotion_failed_accept_cannot_find_request_user) }));
    }

    [Test]
    public void Cancel_PartnerNotInTheField_IsDropped() {
        Send(a, writer => {
            writer.WriteByte(6);
            writer.WriteInt(Emote);
            writer.WriteLong(987654);
        });

        Assert.That(Emotes(b), Is.Empty);
    }

    [Test]
    public void UnknownCommand_IsIgnored() {
        Assert.DoesNotThrow(() => Send(a, writer => writer.WriteByte(5)));
        Assert.Multiple(() => {
            Assert.That(Emotes(a), Is.Empty);
            Assert.That(Emotes(b), Is.Empty);
        });
    }
}

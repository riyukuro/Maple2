using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The mentoring requests: register as mentee (4) or mentor (8), invite a mentee (5), answer a mentor (6, 7).
public class MentorHandlerTests {
    private const byte RegisterMentee = 4;
    private const byte Invite = 5;
    private const byte Accept = 6;
    private const byte Decline = 7;
    private const byte RegisterMentor = 8;

    private TestGameSession test = null!;
    private TestGameSession otherTest = null!;
    private readonly MentorHandler handler = new();
    private GameSession session => test.Session;
    private GameSession other => otherTest.Session;

    private void Login(bool season) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        otherTest = new TestGameSession(options);
        foreach (TestGameSession each in new[] { test, otherTest }) {
            each.AddMap(TestGameSession.Map(2000023));
            if (season) {
                each.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: 1, QuestIds: [], RequiredUserValue: 0,
                    DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 0, RequiredLevel: 0));
            }
            each.Start();
            each.EnterMap(TestGameSession.Map(2000023));
            each.TakeSent();
        }
    }

    private void Send(GameSession from, byte command, Action<Maple2.PacketLib.Tools.ByteWriter>? body = null) {
        handler.Handle(from, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    private static string[] Mentor(TestGameSession from) {
        return from.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mentor)
            .Select(packet => Convert.ToHexString(packet, 2, packet.Length - 2)).ToArray();
    }

    private static string Role(MentorRole role, GameSession of) {
        return "01" + ((byte) role).ToString("X2") + Convert.ToHexString(BitConverter.GetBytes(of.Player.ObjectId));
    }

    private static string Error(MentorError error) => "10" + ((byte) error).ToString("X2") + "00000000";

    // The other player as the world knows it, with the role its session holds.
    private void WorldKnowsOther() {
        test.World.AddPlayer(other.AccountId, other.CharacterId, other.PlayerName);
        test.World.Players[other.CharacterId].MentorRole = (int) other.Player.Value.Character.MentorRole;
    }

    [Test]
    public void RegisterMentee_WithoutASeasonIsRefused() {
        Login(season: false);
        Send(session, RegisterMentee);

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.IsNotReturnUser) }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.None));
        Assert.That(test.World.PlayerUpdates.Any(update => update.HasMentorRole), Is.False);
    }

    [Test]
    public void RegisterMentee_StoresTheRoleAndTellsTheWorld() {
        Login(season: true);
        Send(session, RegisterMentee);

        Assert.That(Mentor(test), Is.EqualTo(new[] { Role(MentorRole.Mentee, session), "04" }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
        Assert.That(test.World.PlayerUpdates.Single(update => update.HasMentorRole).MentorRole, Is.EqualTo((int) MentorRole.Mentee));
    }

    [Test]
    public void RegisterMentor_AnswersWithTheMentorNotice() {
        Login(season: true);
        Send(session, RegisterMentor);

        Assert.That(Mentor(test), Is.EqualTo(new[] { Role(MentorRole.Mentor, session), "08" }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentor));
    }

    [Test]
    public void RegisterMentor_WithoutASeasonIsDropped() {
        Login(season: false);
        Send(session, RegisterMentor);

        Assert.That(Mentor(test), Is.Empty);
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.None));
    }

    [Test]
    public void Register_TwiceIsDropped() {
        Login(season: true);
        Send(session, RegisterMentee);
        Mentor(test);
        Send(session, RegisterMentee);
        Send(session, RegisterMentor);

        Assert.That(Mentor(test), Is.Empty);
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
        Assert.That(test.World.PlayerUpdates.Count(update => update.HasMentorRole), Is.EqualTo(1));
    }

    [Test]
    public void Invite_UnknownNameIsRefused() {
        Login(season: true);
        Send(session, RegisterMentor);
        Mentor(test);
        Send(session, Invite, writer => {
            writer.WriteLong(0);
            writer.WriteUnicodeString("nobody");
        });

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.TargetNotExists) }));
    }

    [Test]
    public void Invite_SelfIsRefused() {
        Login(season: true);
        Send(session, RegisterMentor);
        Mentor(test);
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        Send(session, Invite, writer => {
            writer.WriteLong(0);
            writer.WriteUnicodeString(session.PlayerName);
        });

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.InvalidTarget) }));
    }

    [Test]
    public void Invite_APlayerWhoIsNotAMenteeIsRefused() {
        Login(season: true);
        Send(session, RegisterMentor);
        Mentor(test);
        WorldKnowsOther();
        Send(session, Invite, writer => {
            writer.WriteLong(0);
            writer.WriteUnicodeString(other.PlayerName);
        });

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.TargetIsNotMentee) }));
    }

    [Test]
    public void Invite_AMenteeGetsNoRefusal() {
        Login(season: true);
        Send(session, RegisterMentor);
        Send(other, RegisterMentee);
        Mentor(test);
        WorldKnowsOther();
        Send(session, Invite, writer => {
            writer.WriteLong(0);
            writer.WriteUnicodeString(other.PlayerName);
        });

        Assert.That(Mentor(test), Is.Empty);
    }

    [Test]
    public void Invite_FromAPlayerWhoIsNoMentorIsDropped() {
        Login(season: true);
        WorldKnowsOther();
        Send(session, Invite, writer => {
            writer.WriteLong(0);
            writer.WriteUnicodeString(other.PlayerName);
        });

        Assert.That(Mentor(test), Is.Empty);
    }

    [TestCase(Accept)]
    [TestCase(Decline)]
    public void Answer_ToARequestTheServerDoesNotHoldIsRefused(byte command) {
        Login(season: true);
        Send(session, command, writer => writer.WriteLong(12345));

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.TargetNotExists) }));
    }
}

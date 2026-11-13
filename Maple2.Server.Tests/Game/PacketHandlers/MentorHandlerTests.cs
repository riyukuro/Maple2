using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The mentoring requests: register as mentee (4) or mentor (8), invite a mentee (5), answer a mentor (6, 7), the
// stored requests and pairs at login. The limits are KMS2's constants.xml values.
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

    private DbContextOptions options = null!;

    private void Login(bool season) {
        options = new DbContextOptionsBuilder()
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
            each.SetServerTable(each.ServerTableMetadata.ConstantsTable with { MentorMaxWaitingCount = 100, MenteeMaxReceivedCount = 20 });
            each.World.MentoringWorld = Deliver;
            each.TakeSent();
        }
    }

    // The world hands the change to the receiver's game server (ChannelService.Mentoring).
    private void Deliver(MentoringRequest request) {
        GameSession? receiver = request.ReceiverId == session.CharacterId ? session
            : request.ReceiverId == other.CharacterId ? other : null;
        switch (request.MentoringCase) {
            case MentoringRequest.MentoringOneofCase.Requested:
                receiver?.Mentoring.ReceiveRequest(request.Requested.RequestId);
                break;
            case MentoringRequest.MentoringOneofCase.Reload:
                receiver?.Mentoring.Reload();
                break;
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

    // The session as mentor and the other player as mentee, each known to the other's world.
    private void MentorAndMentee() {
        Login(season: true);
        Send(session, RegisterMentor);
        Send(other, RegisterMentee);
        WorldKnowsOther();
        otherTest.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        otherTest.World.Players[session.CharacterId].MentorRole = (int) MentorRole.Mentor;
        Mentor(test);
        Mentor(otherTest);
    }

    private void InviteOther() {
        Send(session, Invite, writer => {
            writer.WriteLong(0);
            writer.WriteUnicodeString(other.PlayerName);
        });
    }

    private IList<MentoringEntry> Rows() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMentoring(other.CharacterId);
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter writer) => Convert.ToHexString(writer.Buffer, 2, writer.Length - 2);

    // The row as the viewer's packets show it, with the partner the viewer's world knows.
    private static MentorPacket.Entry Entry(GameSession viewer, long id, GameSession partner) {
        Assert.That(viewer.PlayerInfo.GetOrFetch(partner.CharacterId, out PlayerInfo? info), Is.True);
        return new MentorPacket.Entry(id, info!);
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

        Assert.That(Mentor(test), Is.Empty);
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
        Assert.That(test.World.PlayerUpdates.Count(update => update.HasMentorRole), Is.EqualTo(1));
    }

    [TestCase(RegisterMentee, RegisterMentor, MentorRole.Mentee)]
    [TestCase(RegisterMentor, RegisterMentee, MentorRole.Mentor)]
    public void Register_TheOtherRoleIsRefusedWithANotice(byte first, byte second, MentorRole kept) {
        Login(season: true);
        Send(session, first);
        Mentor(test);
        Send(session, second);

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.InvalidTarget) }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(kept));
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
    public void Invite_AMenteeIsStoredAndBothAreTold() {
        MentorAndMentee();
        InviteOther();

        MentoringEntry row = Rows().Single();
        Assert.That(row, Is.EqualTo(new MentoringEntry(row.Id, session.CharacterId, other.CharacterId, false)));
        Assert.That(Mentor(test), Is.EqualTo(new[] { Hex(MentorPacket.MenteeInvite(other.CharacterId, other.PlayerName)) }));
        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { Hex(MentorPacket.ReceiveMenteeInvite(Entry(other, row.Id, session))) }));
    }

    [Test]
    public void Invite_TheSameMenteeTwiceIsRefused() {
        MentorAndMentee();
        InviteOther();
        Mentor(test);
        InviteOther();

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.AlreadyRequestedToMentee) }));
        Assert.That(Rows(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Invite_AMenteeWithAMentorIsRefused() {
        MentorAndMentee();
        InviteOther();
        Send(other, Accept, writer => writer.WriteLong(Rows().Single().Id));
        Mentor(test);
        InviteOther();

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.MenteeHasAlreadyMentor) }));
        Assert.That(Rows(), Has.Count.EqualTo(1));
    }

    // The limits are used as the table gives them: 0 refuses every request.
    [TestCase(0, 20, MentorError.MentorHasMaxCountWaiting)]
    [TestCase(100, 0, MentorError.MenteeHasMaxCountReceived)]
    public void Invite_OverALimitIsRefused(int waiting, int received, MentorError error) {
        MentorAndMentee();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MentorMaxWaitingCount = waiting, MenteeMaxReceivedCount = received });
        InviteOther();

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(error) }));
        Assert.That(Rows(), Is.Empty);
        Assert.That(test.World.MentoringRequests, Is.Empty);
    }

    // Without a season no request is stored or sent; the checks answer as before.
    [Test]
    public void Invite_WithoutASeasonStoresNothing() {
        Login(season: false);
        session.Player.Value.Character.MentorRole = MentorRole.Mentor;
        other.Player.Value.Character.MentorRole = MentorRole.Mentee;
        WorldKnowsOther();
        InviteOther();

        Assert.That(Mentor(test), Is.Empty);
        Assert.That(Mentor(otherTest), Is.Empty);
        Assert.That(Rows(), Is.Empty);
        Assert.That(test.World.MentoringRequests, Is.Empty);
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

    [Test]
    public void Accept_StoresThePairAndTellsTheMentor() {
        MentorAndMentee();
        InviteOther();
        Mentor(test);
        Mentor(otherTest);
        long id = Rows().Single().Id;
        Send(other, Accept, writer => writer.WriteLong(id));

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { Hex(MentorPacket.AcceptMentorInvite(id)) }));
        Assert.That(Rows().Single().Accepted, Is.True);
        MentorPacket.Entry[] pair = [Entry(session, id, other)];
        Assert.That(Mentor(test), Is.EqualTo(new[] { Hex(MentorPacket.MyList(pair)), Hex(MentorPacket.PairOnline(pair)) }));
    }

    // Only the mentee the request names answers it: the mentor's own accept or decline of it is refused.
    [TestCase(Accept)]
    [TestCase(Decline)]
    public void Answer_ToARequestOfAnotherPlayerIsRefused(byte command) {
        MentorAndMentee();
        InviteOther();
        Mentor(test);
        MentoringEntry row = Rows().Single();
        Send(session, command, writer => writer.WriteLong(row.Id));

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.TargetNotExists) }));
        Assert.That(Rows(), Is.EqualTo(new[] { row }));
    }

    [Test]
    public void Decline_RemovesTheRequest() {
        MentorAndMentee();
        InviteOther();
        Mentor(otherTest);
        long id = Rows().Single().Id;
        Send(other, Decline, writer => writer.WriteLong(id));

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { Hex(MentorPacket.DeclineMentorInvite(id)) }));
        Assert.That(Rows(), Is.Empty);
    }

    // Without a season a stored request is not answered and stays.
    [TestCase(Accept)]
    [TestCase(Decline)]
    public void Answer_WithoutASeasonChangesNothing(byte command) {
        Login(season: false);
        MentoringEntry? row;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.AddMentoringRequest(session.CharacterId, other.CharacterId, 3, 100, 20, out row);
        }
        Send(other, command, writer => writer.WriteLong(row!.Id));

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { Error(MentorError.TargetNotExists) }));
        Assert.That(Rows(), Is.EqualTo(new[] { row }));
        Assert.That(otherTest.World.MentoringRequests, Is.Empty);
    }

    [Test]
    public void Load_SendsTheRequestsThePairsAndWhoIsOnline() {
        MentorAndMentee();
        InviteOther();
        long id = Rows().Single().Id;
        Mentor(otherTest);
        other.Mentoring.Load();

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] {
            Hex(MentorPacket.MenteeInvitations(Entry(other, id, session))), "0300000000", "0F00000000",
        }));

        Send(other, Accept, writer => writer.WriteLong(id));
        Mentor(test);
        session.Mentoring.Load();
        MentorPacket.Entry[] pair = [Entry(session, id, other)];
        Assert.That(Mentor(test), Is.EqualTo(new[] { "0200000000", Hex(MentorPacket.MyList(pair)), Hex(MentorPacket.PairOnline(pair)) }));
    }

    [Test]
    public void Load_WithoutASeasonSendsTheEmptyPairListOnly() {
        Login(season: false);
        using (GameStorage.Request db = test.Storage.Context()) {
            db.AddMentoringRequest(session.CharacterId, other.CharacterId, 3, 100, 20, out _);
        }
        other.Mentoring.Load();

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { "0300000000" }));
    }

    // The store: accepting removes the mentee's other requests; a mentor with all its mentees takes no request, and
    // an accept of one removes it.
    [Test]
    public void Store_AcceptRemovesTheOtherRequestsAndKeepsTheMentorLimit() {
        Login(season: true);
        using GameStorage.Request db = test.Storage.Context();
        long mentee = other.CharacterId;
        Assert.That(db.AddMentoringRequest(session.CharacterId, mentee, 3, 100, 20, out MentoringEntry? first), Is.Null);
        Assert.That(db.AddMentoringRequest(424242, mentee, 3, 100, 20, out MentoringEntry? second), Is.Null);
        Assert.That(db.AcceptMentoring(first!.Id, mentee, 3, out _), Is.Null);
        Assert.That(db.GetMentoring(mentee).Select(row => row.Id), Is.EqualTo(new[] { first.Id }));
        Assert.That(db.GetMentoringEntry(second!.Id), Is.Null);

        Assert.That(db.AddMentoringRequest(session.CharacterId, 515151, 1, 100, 20, out _), Is.EqualTo(MentorError.InvalidTarget));
        Assert.That(db.AddMentoringRequest(session.CharacterId, 515151, 2, 100, 20, out MentoringEntry? third), Is.Null);
        Assert.That(db.AcceptMentoring(third!.Id, 515151, 1, out _), Is.EqualTo(MentorError.InvalidTarget));
        Assert.That(db.GetMentoringEntry(third.Id), Is.Null);
    }

    // A request another game server stored while this mentee's pair was accepted (its "has a mentor" check ran
    // first): the accept answers 7 and removes it, and the list shows the ones left.
    [Test]
    public void Accept_ARequestLeftAfterThePairIsRemoved() {
        MentorAndMentee();
        InviteOther();
        long id = Rows().Single().Id;
        Send(other, Accept, writer => writer.WriteLong(id));
        long stuck;
        using (var context = new Ms2Context(options)) {
            var row = new Maple2.Database.Model.Mentoring { MentorId = 424242, MenteeId = other.CharacterId, CreationTime = DateTime.UtcNow };
            context.Mentoring.Add(row);
            context.SaveChanges();
            stuck = row.Id;
        }
        Mentor(otherTest);
        Send(other, Accept, writer => writer.WriteLong(stuck));

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { Error(MentorError.MenteeHasAlreadyMentor), "0200000000" }));
        Assert.That(Rows().Select(row => row.Id), Is.EqualTo(new[] { id }));
    }

    // A partner's logout and login after the pair list was sent: Mentor 15 with that partner alone, once per change.
    [Test]
    public void PairOnline_APartnersLoginAndLogoutArePushed() {
        MentorAndMentee();
        InviteOther();
        long id = Rows().Single().Id;
        Send(other, Accept, writer => writer.WriteLong(id));
        session.Mentoring.Load();
        Mentor(test);

        Channel(-1);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Hex(MentorPacket.PairOnline((other.CharacterId, false))) }));
        Channel(-1);
        Assert.That(Mentor(test), Is.Empty);
        Channel(1);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Hex(MentorPacket.PairOnline((other.CharacterId, true))) }));

        session.Mentoring.Dispose();
        Channel(-1);
        Assert.That(Mentor(test), Is.Empty);

        void Channel(int channel) {
            session.PlayerInfo.ReceiveUpdate(new PlayerUpdateRequest {
                AccountId = other.AccountId,
                CharacterId = other.CharacterId,
                Channel = channel,
            });
        }
    }
}

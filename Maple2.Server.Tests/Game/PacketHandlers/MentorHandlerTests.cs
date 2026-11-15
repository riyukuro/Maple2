using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Manager;
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

    // A scratch MySQL game database (MS2_TEST_GAME_DB) for the tests of a rollback the in-memory store cannot show.
    private static readonly string? MySqlConnection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private void Login(bool season, int[]? questIds = null, int daysInactive = 0, bool mySql = false) {
        options = mySql
            ? new DbContextOptionsBuilder().UseMySql(MySqlConnection, ServerVersion.AutoDetect(MySqlConnection)).Options
            : new DbContextOptionsBuilder()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        test = new TestGameSession(options);
        otherTest = new TestGameSession(options);
        foreach (TestGameSession each in new[] { test, otherTest }) {
            each.AddMap(TestGameSession.Map(2000023));
            if (season) {
                each.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: 1, QuestIds: questIds ?? [], RequiredUserValue: 0,
                    DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: daysInactive, RequiredLevel: 0));
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
            // The game server takes them on the field's thread (ScheduleTakeMentorCoins); here at once.
            case MentoringRequest.MentoringOneofCase.Coins:
                receiver?.Mentoring.TakeMentorCoins();
                break;
        }
    }

    private void Send(GameSession from, byte command, Action<Maple2.PacketLib.Tools.ByteWriter>? body = null) {
        // A registration comes from a register page, which the client shows only to the matching candidate; in a
        // season every player with no role is one from its login (MentoringManager.Load, tested below).
        if (command is RegisterMentee or RegisterMentor && from.Player.Value.Character.MentorRole == MentorRole.None
            && from.FindEvent(GameEventType.ReturnUser).Any()) {
            from.Player.Value.Character.MentorRole = command == RegisterMentee ? MentorRole.MenteeCandidate : MentorRole.MentorCandidate;
        }
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
    private void MentorAndMentee(bool mySql = false) {
        Login(season: true, mySql: mySql);
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
        return db.GetMentoring(other.CharacterId, 1);
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

        Assert.That(Mentor(test), Is.EqualTo(new[] { Role(MentorRole.Mentee, session), "04", Hex(MentorPacket.AccountCharacters(0, session.CharacterId)) }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
        Assert.That(test.World.PlayerUpdates.Single(update => update.HasMentorRole).MentorRole, Is.EqualTo((int) MentorRole.Mentee));
    }

    // The season's quests start with the role already set: a quest requiring mentee (KR 70600057) is accepted.
    [Test]
    public void RegisterMentee_StartsTheSeasonQuestsThatRequireTheRole() {
        const int menteeQuest = 70600057;
        Login(season: true, questIds: [menteeQuest]);
        QuestMetadata quest = TestGameSession.QuestData(menteeQuest);
        test.Register(quest with { Require = quest.Require with { Mentee = true } });
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        Send(session, RegisterMentee);

        Assert.That(session.Quest.TryGetQuest(menteeQuest, out Quest? started), Is.True);
        Assert.That(started!.State, Is.EqualTo(QuestState.Started));
    }

    [Test]
    public void RegisterMentor_AnswersWithTheMentorNotice() {
        Login(season: true);
        Send(session, RegisterMentor);

        Assert.That(Mentor(test), Is.EqualTo(new[] { Role(MentorRole.Mentor, session), "08", Hex(MentorPacket.AccountCharacters(session.CharacterId, 0)) }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentor));
    }

    [Test]
    public void RegisterMentor_WithoutASeasonIsRefused() {
        Login(season: false);
        Send(session, RegisterMentor);

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.InvalidTarget) }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.None));
    }

    // The client's menu opens the register page only from MentorRequireLevel (mentor) or MenteeRequireLevel (mentee)
    // of constants.xml (KR 50 and 30, uigamemenudialog.cpp:2078-2090): one level lower gets the refusal and keeps
    // its candidate role, the level itself registers.
    [TestCase(RegisterMentor, MentorRole.Mentor, MentorError.InvalidTarget)]
    [TestCase(RegisterMentee, MentorRole.Mentee, MentorError.IsNotReturnUser)]
    public void Register_BelowTheRequiredLevelIsRefused(byte command, MentorRole role, MentorError error) {
        Login(season: true);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MentorRequireLevel = 50, MenteeRequireLevel = 30 });
        short required = (short) (role == MentorRole.Mentor ? 50 : 30);
        session.Player.Value.Character.Level = (short) (required - 1);
        Send(session, command);

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(error) }));
        Assert.That(session.Player.Value.Character.MentorRole,
            Is.EqualTo(role == MentorRole.Mentor ? MentorRole.MentorCandidate : MentorRole.MenteeCandidate));
        Assert.That(test.World.PlayerUpdates.Any(update => update.HasMentorRole), Is.False);

        session.Player.Value.Character.Level = required;
        Send(session, command);
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(role));
    }

    // The season's login sets the candidate role before the field entry: a character away for the season's days is
    // a mentee candidate (1), and stays one at a later entry of the season; one back sooner, or never saved, is a
    // mentor candidate (4). The world learns the role. Before, the role stayed none, so the client's dialog had no
    // tab and neither register page could be reached.
    [Test]
    public void Load_SetsTheCandidateRole() {
        Login(season: true, daysInactive: 30);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.EntryLastSave = now - 31 * 86400L;
        other.EntryLastSave = now - 29 * 86400L;
        session.Mentoring.Load();
        other.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MenteeCandidate));
        Assert.That(other.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MentorCandidate));
        Assert.That(test.World.PlayerUpdates.Single(update => update.HasMentorRole).MentorRole, Is.EqualTo((int) MentorRole.MenteeCandidate));

        // A later entry of the season (a channel move, a new login) keeps the return.
        session.EntryLastSave = now;
        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MenteeCandidate));

        // The mentee candidate registers as a mentee; the mentor candidate's mentee request is refused.
        test.TakeSent();
        otherTest.TakeSent();
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(RegisterMentee)));
        handler.Handle(other, TestGameSession.Packet(writer => writer.WriteByte(RegisterMentee)));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { Error(MentorError.IsNotReturnUser) }));
        Assert.That(other.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MentorCandidate));
    }

    // A registration from a player the season's login did not make the matching candidate is refused: a mentee
    // request without role 1 gets error 1, a mentor request without role 4 error 3; the role stays.
    [TestCase(RegisterMentee, MentorRole.None, MentorError.IsNotReturnUser)]
    [TestCase(RegisterMentee, MentorRole.MentorCandidate, MentorError.IsNotReturnUser)]
    [TestCase(RegisterMentor, MentorRole.None, MentorError.InvalidTarget)]
    [TestCase(RegisterMentor, MentorRole.MenteeCandidate, MentorError.InvalidTarget)]
    public void Register_WithoutTheCandidateRoleIsRefused(byte command, MentorRole held, MentorError error) {
        Login(season: true);
        session.Player.Value.Character.MentorRole = held;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(command)));
        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(error) }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(held));
        Assert.That(test.World.PlayerUpdates.Any(update => update.HasMentorRole), Is.False);
    }

    // Without a season every role goes back to none at login (the dialog does not open then): a registration ends
    // with its season.
    [TestCase(MentorRole.MenteeCandidate, MentorRole.None)]
    [TestCase(MentorRole.MentorCandidate, MentorRole.None)]
    [TestCase(MentorRole.Mentee, MentorRole.None)]
    [TestCase(MentorRole.UnregisteredMentor, MentorRole.None)]
    public void Load_WithoutASeasonDropsTheRole(MentorRole held, MentorRole after) {
        Login(season: false);
        session.Player.Value.Character.MentorRole = held;
        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(after));
    }

    // The season's date: a last save before it returns, one after it does not; the days count back from now. A
    // character never saved, or a row with neither, has no return.
    [Test]
    public void Returning_ReadsTheDateAndDays() {
        var since = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long at = since.ToUnixTimeSeconds();
        Assert.That(MentoringManager.Returning(since, 0, at - 1, at + 100), Is.True);
        Assert.That(MentoringManager.Returning(since, 0, at + 1, at + 100), Is.False);
        Assert.That(MentoringManager.Returning(DateTimeOffset.MinValue, 7, 1000, 1000 + 7 * 86400L), Is.True);
        Assert.That(MentoringManager.Returning(DateTimeOffset.MinValue, 7, 1000, 999 + 7 * 86400L), Is.False);
        Assert.That(MentoringManager.Returning(DateTimeOffset.MinValue, 7, 0, 1000 + 7 * 86400L), Is.False);
        Assert.That(MentoringManager.Returning(DateTimeOffset.MinValue, 0, 1, 1000000), Is.False);
    }

    // A ReturnUserCandidate row (KR 180105: days 30, the next season 2 until that season's end) keeps a return made
    // while it runs for the next season, which starts after the row has ended: the character is that season's
    // mentee candidate at an entry with no days away. Without the row it is a mentor candidate.
    [TestCase(true)]
    [TestCase(false)]
    public void CandidateWindow_KeepsTheReturnForTheNextSeason(bool window) {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var first = new TestGameSession(options);
        first.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: 1, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 30, RequiredLevel: 0));
        if (window) {
            first.AddEvent(78, GameEventType.ReturnUserCandidate, new ReturnUserCandidate(Season: 2, SeasonId: 10004, MinLevel: 30,
                UnknownDate: DateTimeOffset.UtcNow.AddDays(90)), end: DateTime.Now.AddDays(1));
        }
        first.AddMap(TestGameSession.Map(2000023));
        first.Start();
        first.Session.EntryLastSave = now - 31 * 86400L;
        first.Session.Mentoring.Load();
        Assert.That(first.Session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MenteeCandidate));

        // The next season, after the window: the same character (its stored rows moved to the new session's
        // character), back the day before.
        var next = new TestGameSession(options);
        using (var context = new Ms2Context(options)) {
            foreach (Maple2.Database.Model.GameEventUserValue row in context.GameEventUserValue.AsNoTracking()
                         .Where(row => row.CharacterId == first.CharacterId).ToList()) {
                row.CharacterId = next.CharacterId;
                context.GameEventUserValue.Add(row);
            }
            context.SaveChanges();
        }
        next.AddEvent(79, GameEventType.ReturnUser, new ReturnUser(SeasonId: 2, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 30, RequiredLevel: 0));
        next.AddMap(TestGameSession.Map(2000023));
        next.Start();
        next.Session.EntryLastSave = now - 86400L;
        next.Session.Mentoring.Load();
        Assert.That(next.Session.Player.Value.Character.MentorRole, Is.EqualTo(window ? MentorRole.MenteeCandidate : MentorRole.MentorCandidate));
    }

    // A stamp ReturnUser row (KR/NA 180112: value1 a date, no value3) running inside a mentoring season (180110,
    // season 5, 30 days) is no season: the character 31 days away is the season's mentee candidate, its return is
    // stored for season 5, and its mentee registration is taken.
    [Test]
    public void StampReturnUserRow_IsNotTheSeason() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.AddEvent(70, GameEventType.ReturnUser, new ReturnUser(SeasonId: 0, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: new DateTimeOffset(2018, 3, 31, 0, 0, 0, TimeSpan.Zero), DaysInactive: 0, RequiredLevel: 0));
        test.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: 5, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 30, RequiredLevel: 0));
        test.AddMap(TestGameSession.Map(2000023));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MentorMaxWaitingCount = 100, MenteeMaxReceivedCount = 20 });
        session.EntryLastSave = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 31 * 86400L;
        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MenteeCandidate));
        Assert.That(session.GameEvent.HasValue(GameEventUserValueType.MentoringReturnUser, "5", DateTimeOffset.UtcNow.ToUnixTimeSeconds()), Is.True);

        test.TakeSent();
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(RegisterMentee)));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
    }

    // A return stored while the season was to end in a day is kept to the season's later end once the event is
    // extended, at an entry with no days away.
    [Test]
    public void StoredReturn_FollowsAnExtendedSeason() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var first = new TestGameSession(options);
        first.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: 1, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 30, RequiredLevel: 0), end: DateTime.Now.AddDays(1));
        first.AddMap(TestGameSession.Map(2000023));
        first.Start();
        first.Session.EntryLastSave = now - 31 * 86400L;
        first.Session.Mentoring.Load();

        var next = new TestGameSession(options);
        using (var context = new Ms2Context(options)) {
            foreach (Maple2.Database.Model.GameEventUserValue row in context.GameEventUserValue.AsNoTracking()
                         .Where(row => row.CharacterId == first.CharacterId).ToList()) {
                row.CharacterId = next.CharacterId;
                context.GameEventUserValue.Add(row);
            }
            context.SaveChanges();
        }
        DateTime end = DateTime.Now.AddDays(10);
        next.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: 1, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 30, RequiredLevel: 0), end: end);
        next.AddMap(TestGameSession.Map(2000023));
        next.Start();
        next.Session.EntryLastSave = now - 86400L;
        next.Session.Mentoring.Load();
        Assert.That(next.Session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MenteeCandidate));
        Assert.That(next.Session.GameEvent.HasValue(GameEventUserValueType.MentoringReturnUser, "1", now + 5 * 86400L), Is.True);
    }

    // A return stored on the season's row (77) or on a window (78, no longer running), whose stored end has passed,
    // while season 1 runs on, extended: the first entry after the old end is still the season's mentee candidate.
    // Another season's return is not kept.
    [TestCase(77, 1, MentorRole.MenteeCandidate)]
    [TestCase(78, 1, MentorRole.MenteeCandidate)]
    [TestCase(78, 2, MentorRole.MentorCandidate)]
    public void StoredReturn_PastItsOldEnd_FollowsTheExtendedSeason(int eventId, int storedSeason, MentorRole expected) {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var next = new TestGameSession(options);
        using (var context = new Ms2Context(options)) {
            context.GameEventUserValue.Add(new Maple2.Database.Model.GameEventUserValue {
                CharacterId = next.CharacterId,
                EventId = eventId,
                Type = GameEventUserValueType.MentoringReturnUser,
                Value = storedSeason.ToString(),
                ExpirationTime = now - 3600,
            });
            context.SaveChanges();
        }
        next.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: 1, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 30, RequiredLevel: 0), end: DateTime.Now.AddDays(10));
        next.AddMap(TestGameSession.Map(2000023));
        next.Start();
        next.Session.EntryLastSave = now - 86400L;
        next.Session.Mentoring.Load();

        Assert.That(next.Session.Player.Value.Character.MentorRole, Is.EqualTo(expected));
        Assert.That(next.Session.GameEvent.HasValue(GameEventUserValueType.MentoringReturnUser, "1", now + 5 * 86400L),
            Is.EqualTo(expected == MentorRole.MenteeCandidate));
    }

    [Test]
    public void Register_TwiceAnswersTheNoticeAgain() {
        Login(season: true);
        Send(session, RegisterMentee);
        Mentor(test);
        Send(session, RegisterMentee);

        Assert.That(Mentor(test), Is.EqualTo(new[] { "04" }));
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

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.InvalidTarget) }));
        Assert.That(Mentor(otherTest), Is.Empty);
        Assert.That(Rows(), Is.Empty);
        Assert.That(test.World.MentoringRequests, Is.Empty);
    }

    [Test]
    public void Invite_FromAPlayerWhoIsNoMentorIsRefused() {
        Login(season: true);
        WorldKnowsOther();
        Send(session, Invite, writer => {
            writer.WriteLong(0);
            writer.WriteUnicodeString(other.PlayerName);
        });

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.InvalidTarget) }));
        Assert.That(Rows(), Is.Empty);
    }

    [Test]
    public void RegisterMentor_TwiceAnswersTheNoticeAgain() {
        Login(season: true);
        Send(session, RegisterMentor);
        Mentor(test);
        Send(session, RegisterMentor);

        Assert.That(Mentor(test), Is.EqualTo(new[] { "08" }));
        Assert.That(test.World.PlayerUpdates.Count(update => update.HasMentorRole), Is.EqualTo(1));
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

    // The new mentor's pair reward: one mail with the table's 1000 mentor points, collected as MentorToken. A
    // refused accept sends none.
    [Test]
    public void Accept_MailsTheMentorItsPoints() {
        MentorAndMentee();
        test.Register(TestGameSession.Metadata(90000023));
        otherTest.Register(TestGameSession.Metadata(90000023));
        InviteOther();
        long id = Rows().Single().Id;
        Send(session, Accept, writer => writer.WriteLong(id));
        Assert.That(MentorMails(), Is.Empty);

        Send(other, Accept, writer => writer.WriteLong(id));
        Send(other, Accept, writer => writer.WriteLong(id));

        var mails = MentorMails();
        Assert.Multiple(() => {
            Assert.That(mails.Select(mail => mail.Items), Is.EqualTo(new[] { new List<(int, int)> { (90000023, 1000) } }).AsCollection);
            Assert.That(mails.Single().Title, Does.Contain(nameof(StringCode.s_mentoring_new_mentor_mail_title)));
            Assert.That(otherTest.World.MailNotifications.Select(note => (note.CharacterId, note.MailId)),
                Is.EqualTo(new[] { (session.CharacterId, mails.Single().Id) }));
        });

        session.Mail.Notify(force: true);
        Assert.That(session.Mail.Collect(mails.Single().Id), Is.EqualTo(Maple2.Model.Error.MailError.none));
        Assert.That(session.Player.Value.Currency.MentorToken, Is.EqualTo(1000));
    }

    // The pair and its reward mail are one store: a mail the database refuses refuses the accept (an error, no mail,
    // no pair told). On MySQL the pair rolls back too and the request still waits (Accept_MailRefused_KeepsTheRequest).
    [Test]
    public void Accept_MailRefused_RefusesTheAccept() {
        MentorAndMentee();
        test.Register(TestGameSession.Metadata(90000023));
        otherTest.Register(TestGameSession.Metadata(90000023));
        InviteOther();
        long id = Rows().Single().Id;
        Mentor(otherTest);
        otherTest.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Mail>().Any(entry => entry.State == EntityState.Added);

        Send(other, Accept, writer => writer.WriteLong(id));
        otherTest.FailSave = null;

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { Error(MentorError.TargetNotExists) }));
        Assert.That(MentorMails(), Is.Empty);
        Assert.That(otherTest.World.MentoringRequests.Any(request => request.Reload != null), Is.False);
    }

    [Test]
    public void Accept_MailRefused_KeepsTheRequest() {
        if (MySqlConnection == null) {
            Assert.Ignore("the accept's rollback needs a real database: set MS2_TEST_GAME_DB");
        }
        MentorAndMentee(mySql: true);
        test.Register(TestGameSession.Metadata(90000023));
        otherTest.Register(TestGameSession.Metadata(90000023));
        InviteOther();
        long id = Rows().Single().Id;
        otherTest.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Mail>().Any(entry => entry.State == EntityState.Added);

        Send(other, Accept, writer => writer.WriteLong(id));
        otherTest.FailSave = null;

        Assert.That(Rows().Single(), Is.EqualTo(new MentoringEntry(id, session.CharacterId, other.CharacterId, false)));
        Assert.That(MentorMails(), Is.Empty);

        Send(other, Accept, writer => writer.WriteLong(id));
        Assert.That(Rows().Single().Accepted, Is.True);
        Assert.That(MentorMails(), Has.Count.EqualTo(1));
    }

    private List<(long Id, string Title, List<(int Id, int Amount)> Items)> MentorMails() {
        using var context = new Ms2Context(options);
        return context.Mail.AsNoTracking().Where(mail => mail.ReceiverId == session.CharacterId).ToList()
            .Select(mail => (mail.Id, mail.Title, context.Item.AsNoTracking().Where(item => item.OwnerId == mail.Id).ToList()
                .Select(item => (item.ItemId, item.Amount)).ToList()))
            .ToList();
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
            db.AddMentoringRequest(session.CharacterId, other.CharacterId, 1, 3, 100, 20, out row);
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
            AttendDate(0, 0), // the mentee's attend streak, none claimed yet
            Hex(MentorPacket.AccountCharacters(0, other.CharacterId)), // the account's registered characters
        }));

        Send(other, Accept, writer => writer.WriteLong(id));
        Mentor(test);
        session.Mentoring.Load();
        MentorPacket.Entry[] pair = [Entry(session, id, other)];
        Assert.That(Mentor(test), Is.EqualTo(new[] { "0200000000", Hex(MentorPacket.MyList(pair)), Hex(MentorPacket.PairOnline(pair)),
            Hex(MentorPacket.AccountCharacters(session.CharacterId, 0)) }));
    }

    [Test]
    public void Load_WithoutASeasonSendsTheEmptyPairListAndNoCharacters() {
        Login(season: false);
        using (GameStorage.Request db = test.Storage.Context()) {
            db.AddMentoringRequest(session.CharacterId, other.CharacterId, 1, 3, 100, 20, out _);
        }
        other.Mentoring.Load();

        Assert.That(Mentor(otherTest), Is.EqualTo(new[] { "0300000000", Hex(MentorPacket.AccountCharacters(0, 0)) }));
    }

    // The store: accepting removes the mentee's other requests; a mentor with all its mentees takes no request, and
    // an accept of one removes it.
    [Test]
    public void Store_AcceptRemovesTheOtherRequestsAndKeepsTheMentorLimit() {
        Login(season: true);
        using GameStorage.Request db = test.Storage.Context();
        long mentee = other.CharacterId;
        Assert.That(db.AddMentoringRequest(session.CharacterId, mentee, 1, 3, 100, 20, out MentoringEntry? first), Is.Null);
        Assert.That(db.AddMentoringRequest(424242, mentee, 1, 3, 100, 20, out MentoringEntry? second), Is.Null);
        Assert.That(db.AcceptMentoring(first!.Id, mentee, 1, 3, out _), Is.Null);
        Assert.That(db.GetMentoring(mentee, 1).Select(row => row.Id), Is.EqualTo(new[] { first.Id }));
        Assert.That(db.GetMentoringEntry(second!.Id, 1), Is.Null);

        Assert.That(db.AddMentoringRequest(session.CharacterId, 515151, 1, 1, 100, 20, out _), Is.EqualTo(MentorError.InvalidTarget));
        Assert.That(db.AddMentoringRequest(session.CharacterId, 515151, 1, 2, 100, 20, out MentoringEntry? third), Is.Null);
        Assert.That(db.AcceptMentoring(third!.Id, 515151, 1, 1, out _), Is.EqualTo(MentorError.InvalidTarget));
        Assert.That(db.GetMentoringEntry(third.Id, 1), Is.Null);
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
            var row = new Maple2.Database.Model.Mentoring { MentorId = 424242, MenteeId = other.CharacterId, Season = 1, CreationTime = DateTime.UtcNow };
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

    private const byte Reward = 0;
    private const byte OpenDialog = 9;

    // The dialog's open (Mentor 9) gives the mentor page its coins today: 0 until the next midnight (Mentor 11: an
    // unread int, the coins, the time they hold until), before the dialog itself (Mentor 9).
    [Test]
    public void OpenDialog_SendsTodaysCoinsUntilMidnight() {
        Login(season: true);
        Send(session, RegisterMentor);
        Mentor(test);

        Send(session, OpenDialog);

        string[] sent = Mentor(test);
        string coins = "0B" + "00000000" + "00000000" + Convert.ToHexString(BitConverter.GetBytes(NextMidnight()));
        Assert.That(sent, Has.Member(coins));
        Assert.That(Array.IndexOf(sent, coins), Is.LessThan(Array.IndexOf(sent, "09")));
    }

    private static string AttendDate(int level, long next) => Hex(MentorPacket.AttendDate(level, next));

    private static long NextMidnight() => DateTime.Now.Date.AddDays(1).ToEpochSeconds();

    // Mentor 11: an unread int, the coins today, the time they hold until.
    private static string Coins(int today) => Hex(MentorPacket.DailyPoints(today, NextMidnight()));

    // The reward page's button (Mentor 0): the first day's coin, the streak level and the next midnight (Mentor 10).
    [Test]
    public void AttendReward_GivesTheDaysCoinOnce() {
        Login(season: true);
        Send(session, RegisterMentee);
        Mentor(test);

        Send(session, Reward);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Coins(100), AttendDate(1, NextMidnight()) }));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(100));

        Send(session, Reward);
        Assert.That(Mentor(test), Is.EqualTo(new[] { AttendDate(1, NextMidnight()) }));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(100));
    }

    // The streak is stored at once, so the next login sends it (Mentor 10 after the lists).
    [Test]
    public void AttendReward_TheStoredStreakIsSentAtLogin() {
        Login(season: true);
        Send(session, RegisterMentee);
        Send(session, Reward);
        Mentor(test);

        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetEventUserValues(session.CharacterId).Select(value => (value.Type, value.Value)),
                Is.EquivalentTo(new[] {
                    (GameEventUserValueType.MentoringAttendLevel, "1"),
                    (GameEventUserValueType.MentoringAttendNext, NextMidnight().ToString()),
                    (GameEventUserValueType.MentoringMenteeCoinToday, "100"),
                }));
        }
        session.Mentoring.Load();
        Assert.That(Mentor(test), Has.Member(AttendDate(1, NextMidnight())));
    }

    // The day, the coins today and the token are one store: a claim the database refuses gives no coin, keeps the
    // day (nothing stored, nothing in memory, the old date sent again), and the next claim gives the coin once.
    [Test]
    public void AttendReward_NotStoredGivesNothingAndKeepsTheDay() {
        Login(season: true);
        Send(session, RegisterMentee);
        Mentor(test);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.GameEventUserValue>().Any();
        Send(session, Reward);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(Mentor(test), Is.EqualTo(new[] { AttendDate(0, 0) }));
            Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(0));
            Assert.That(session.GameEvent.Peek(GameEventUserValueType.MentoringAttendLevel, 77), Is.Not.EqualTo("1"));
            Assert.That(session.GameEvent.Peek(GameEventUserValueType.MentoringMenteeCoinToday, 77), Is.Not.EqualTo("100"));
        });
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetEventUserValues(session.CharacterId), Is.Empty);
            Assert.That(db.LoadPlayer(test.AccountId, test.CharacterId, 1, 1)!.Currency.MenteeToken, Is.EqualTo(0));
        }
        test.Errors.Clear();

        Send(session, Reward);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Coins(100), AttendDate(1, NextMidnight()) }));
        Send(session, Reward);
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(100));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.LoadPlayer(test.AccountId, test.CharacterId, 1, 1)!.Currency.MenteeToken, Is.EqualTo(100));
        }
    }

    [Test]
    public void AttendReward_OnlyAMenteeInASeason() {
        Login(season: true);
        Send(session, RegisterMentor);
        Mentor(test);
        Send(session, Reward);
        Assert.That(Mentor(test), Is.Empty);
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(0));

        Login(season: false);
        Send(session, Reward);
        session.Mentoring.Load();
        Assert.That(Mentor(test).Any(packet => packet.StartsWith("0A")), Is.False);
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(0));
    }

    // At the token cap the claim gives nothing and keeps the day (Mentor 16, 9).
    [Test]
    public void AttendReward_AtTheTokenCapIsRefused() {
        Login(season: true);
        Send(session, RegisterMentee);
        session.Currency.Change(CurrencyType.MenteeToken, session.ServerTableMetadata.ConstantsTable.MenteeTokenMax);
        Mentor(test);

        Send(session, Reward);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.FullEarnCoin) }));
        Send(session, Reward);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.FullEarnCoin) }));
    }

    // The mentee coins of the mentoring rewards stop at the table's daily cap (1500): the attend claim gives the
    // part under it, and at the cap it is refused like the token cap (Mentor 16, 9) and keeps the day.
    [Test]
    public void AttendReward_StopsAtTheDailyCoinCap() {
        Login(season: true);
        Send(session, RegisterMentee);
        Assert.That(session.Mentoring.AddMenteeCoin(1450), Is.EqualTo(1450));
        Mentor(test);

        Send(session, Reward);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Coins(1500), AttendDate(1, NextMidnight()) }));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(1500));

        Assert.That(session.Mentoring.AddMenteeCoin(100), Is.EqualTo(0));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(1500));
    }

    [Test]
    public void AttendReward_AtTheDailyCoinCapIsRefused() {
        Login(season: true);
        Send(session, RegisterMentee);
        session.Mentoring.AddMenteeCoin(1500);
        Mentor(test);

        Send(session, Reward);
        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.FullEarnCoin) }));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(1500));
        session.Mentoring.Load();
        Assert.That(Mentor(test), Has.Member(AttendDate(0, 0)));
    }

    // The dialog's open sends a mentee the coins today; without a season the coins are given as before, uncounted.
    [Test]
    public void MenteeCoins_ShownOnOpenAndUncappedWithoutASeason() {
        Login(season: true);
        Send(session, RegisterMentee);
        session.Mentoring.AddMenteeCoin(300);
        Mentor(test);
        Send(session, OpenDialog);
        Assert.That(Mentor(test), Has.Member(Coins(300)));

        Login(season: false);
        Assert.That(session.Mentoring.AddMenteeCoin(2000), Is.EqualTo(2000));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(2000));
    }

    // Only a mentee's coins count against the daily cap: a mentor in a season (whose page shows 0) gets a quest's
    // mentee coin whole. Before, it was cut off at the mentee's cap.
    [Test]
    public void MenteeCoins_AMentorIsNotCapped() {
        Login(season: true);
        Send(session, RegisterMentor);
        Assert.That(session.Mentoring.AddMenteeCoin(2000), Is.EqualTo(2000));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(2000));
    }

    // Near the token cap only the part the cap lets in is given and counted for the day. Before, the day counted
    // the whole coin while the wallet took only that part.
    [Test]
    public void MenteeCoins_CountOnlyWhatTheTokenCapLetsIn() {
        Login(season: true);
        Send(session, RegisterMentee);
        int max = session.ServerTableMetadata.ConstantsTable.MenteeTokenMax;
        session.Currency.Change(CurrencyType.MenteeToken, max - 50);
        Mentor(test);

        Assert.That(session.Mentoring.AddMenteeCoin(100), Is.EqualTo(50));
        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(max));
        Assert.That(Mentor(test), Is.EqualTo(new[] { Coins(50) }));
    }

    // A quest's mentee coin is stored with the completed quest: the token and the day's count. The count was stored
    // on its own and the token left to the next session save, so a stop between kept the quest done and the day's
    // cap used without the coin.
    [Test]
    public void MenteeCoins_OfAQuestAreStoredWithIt() {
        Login(season: true);
        Send(session, RegisterMentee);
        Mentor(test);
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        QuestMetadata data = TestGameSession.QuestData(90000041);
        Quest quest = test.StartQuest(data with { CompleteReward = data.CompleteReward with { MenteeCoin = 100 } });

        Assert.That(session.Quest.Complete(quest), Is.True);

        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(100));
        Assert.That(Mentor(test), Has.Member(Coins(100)));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.LoadPlayer(test.AccountId, test.CharacterId, 1, 1)!.Currency.MenteeToken, Is.EqualTo(100));
        Assert.That(db.GetEventUserValues(session.CharacterId)
            .Single(value => value.Type == GameEventUserValueType.MentoringMenteeCoinToday).Value, Is.EqualTo("100"));
        Assert.That(test.Errors, Is.Empty);
    }

    // A completion the database refuses pays no coin and counts nothing for the day.
    [Test]
    public void MenteeCoins_OfAQuestNotStored_GivesAndCountsNothing() {
        Login(season: true);
        Send(session, RegisterMentee);
        Mentor(test);
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        QuestMetadata data = TestGameSession.QuestData(90000041);
        Quest quest = test.StartQuest(data with { CompleteReward = data.CompleteReward with { MenteeCoin = 100 } });
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.GameEventUserValue>().Any();

        Assert.That(session.Quest.Complete(quest), Is.False);
        test.FailSave = null;

        Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(0));
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.MentoringMenteeCoinToday, 77), Is.Null.Or.Empty.Or.EqualTo("0"));
        Assert.That(quest.State, Is.EqualTo(QuestState.Started));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetEventUserValues(session.CharacterId), Is.Empty);
    }

    // ---- The mentor coins: a paired mentee's quest coins earn its mentor the same, under the mentor's caps ----

    // The session as mentor paired with the other player as mentee.
    private void Paired() {
        MentorAndMentee();
        InviteOther();
        long id = Rows().Single().Id;
        Send(other, Accept, writer => writer.WriteLong(id));
        Mentor(test);
        Mentor(otherTest);
    }

    private IList<(long Id, long Coins)> MentorCoinRows() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMentorCoins(session.CharacterId, 1);
    }

    [Test]
    public void MentorCoins_AMenteesQuestCoinGivesItsMentorTheSame() {
        Paired();
        Assert.That(other.Mentoring.AddMenteeCoin(100), Is.EqualTo(100));

        Assert.That(otherTest.World.MentoringRequests.Where(request => request.Coins != null).Select(request => request.ReceiverId),
            Is.EqualTo(new[] { session.CharacterId }));
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(100));
        Assert.That(Mentor(test), Is.EqualTo(new[] { Coins(100) }));
        Assert.That(MentorCoinRows(), Is.Empty);
        // The dialog's open shows the mentor its coins today (it showed 0 before).
        Send(session, OpenDialog);
        Assert.That(Mentor(test), Has.Member(Coins(100)));
        Assert.That(test.Errors, Is.Empty);
    }

    // A mentee's quest coin and its mentor's coins are one store: a mentor-coin write the database refuses refuses
    // the completion (no mentee coin, no mentor coin, the quest still open). Before, it was logged and the mentor's
    // coins were lost.
    [Test]
    public void MentorCoins_NotStored_RefusesTheQuest() {
        Paired();
        foreach (TestGameSession each in new[] { test, otherTest }) {
            each.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
            each.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
            each.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
            each.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        }
        QuestMetadata data = TestGameSession.QuestData(90000042);
        Quest quest = otherTest.StartQuest(data with { CompleteReward = data.CompleteReward with { MenteeCoin = 100 } });
        otherTest.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Mentoring>().Any(entry => entry.State == EntityState.Modified);

        Assert.That(other.Quest.Complete(quest), Is.False);
        otherTest.FailSave = null;

        Assert.That(other.Currency[CurrencyType.MenteeToken], Is.EqualTo(0));
        Assert.That(MentorCoinRows(), Is.Empty);
        Assert.That(quest.State, Is.EqualTo(QuestState.Started));
        otherTest.Errors.Clear();

        Assert.That(other.Quest.Complete(quest), Is.True);
        Assert.That(other.Currency[CurrencyType.MenteeToken], Is.EqualTo(100));
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(100));
    }

    // The attend coin and the coins of a mentee with no pair earn no mentor coins.
    [Test]
    public void MentorCoins_NotFromTheAttendCoinNorWithoutAPair() {
        MentorAndMentee();
        other.Mentoring.AddMenteeCoin(100);
        Assert.That(otherTest.World.MentoringRequests.Any(request => request.Coins != null), Is.False);

        Paired();
        other.Mentoring.AddMenteeCoin(100, fromQuest: false);
        Assert.That(MentorCoinRows(), Is.Empty);
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(0));
    }

    // The table's mentor cap (maxMentorDailyRewardCoin 400): the part over it is dropped, and the coins today stay
    // at the cap until midnight.
    [Test]
    public void MentorCoins_StopAtTheMentorsDailyCap() {
        Paired();
        other.Mentoring.AddMenteeCoin(300);
        other.Mentoring.AddMenteeCoin(300);
        other.Mentoring.AddMenteeCoin(300);

        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(400));
        Assert.That(Mentor(test), Is.EqualTo(new[] { Coins(300), Coins(400), Coins(400) }));
        Assert.That(MentorCoinRows(), Is.Empty);
    }

    // A mentor the world does not reach (offline, another server down) takes the coins at its next field entry.
    [Test]
    public void MentorCoins_AMentorAwayTakesThemLater() {
        Paired();
        otherTest.World.MentoringWorld = null;
        other.Mentoring.AddMenteeCoin(100);
        other.Mentoring.AddMenteeCoin(150);
        Assert.That(MentorCoinRows().Select(row => row.Coins), Is.EqualTo(new[] { 250L }));
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(0));

        session.Mentoring.TakeMentorCoins();
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(250));
        Assert.That(MentorCoinRows(), Is.Empty);
        // Nothing left: a second take gives nothing.
        session.Mentoring.TakeMentorCoins();
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(250));
    }

    // Near MentorTokenMax only what it lets in is given and counted; the rest is dropped.
    [Test]
    public void MentorCoins_CountOnlyWhatTheTokenCapLetsIn() {
        Paired();
        int max = session.ServerTableMetadata.ConstantsTable.MentorTokenMax;
        session.Currency.Change(CurrencyType.MentorToken, max - 30);
        Mentor(test);

        other.Mentoring.AddMenteeCoin(100);
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(max));
        Assert.That(Mentor(test), Is.EqualTo(new[] { Coins(30) }));
        Assert.That(MentorCoinRows(), Is.Empty);
    }

    // A take that is not stored gives no token, counts nothing and leaves the coins on the pair.
    [Test]
    public void MentorCoins_ATakeNotStoredGivesNothing() {
        Paired();
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Mentoring>()
            .Any(entry => entry.State == EntityState.Modified && entry.Entity.MentorId == session.CharacterId && entry.Entity.MentorCoins == 0);
        otherTest.World.MentoringWorld = null;
        other.Mentoring.AddMenteeCoin(100);
        session.Mentoring.TakeMentorCoins();
        test.FailSave = null;

        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(0));
        Assert.That(MentorCoinRows().Select(row => row.Coins), Is.EqualTo(new[] { 100L }));
        test.Errors.Clear();
        Send(session, OpenDialog);
        Assert.That(Mentor(test), Has.Member(Coins(0)));

        session.Mentoring.TakeMentorCoins();
        Assert.That(session.Currency[CurrencyType.MentorToken], Is.EqualTo(100));
    }

    // ---- One registered character per account for the season, Mentor 12, the season of the rows ----

    // Another character of the session's account, logged in with the season (`seasonId`) running, roles loaded.
    private TestGameSession SameAccount(int seasonId = 1) {
        var second = new TestGameSession(options, accountId: session.AccountId);
        second.AddMap(TestGameSession.Map(2000023));
        second.AddEvent(77, GameEventType.ReturnUser, new ReturnUser(SeasonId: seasonId, QuestIds: [], RequiredUserValue: 0,
            DateInactiveSince: DateTimeOffset.MinValue, DaysInactive: 0, RequiredLevel: 0));
        second.Start();
        second.EnterMap(TestGameSession.Map(2000023));
        second.World.MentoringWorld = Deliver;
        second.Session.Mentoring.Load();
        return second;
    }

    // A mentor registration is the account's for the season: the account's other characters log in with role 6 and
    // Mentor 12 naming the mentor, and their register requests are refused without storing anything.
    [Test]
    public void Register_TheAccountsOtherCharactersGetRole6AndAreRefused() {
        Login(season: true);
        Send(session, RegisterMentor);

        TestGameSession second = SameAccount();
        Assert.That(second.Session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.UnregisteredMentor));
        Assert.That(Mentor(second), Has.Member(Hex(MentorPacket.AccountCharacters(session.CharacterId, 0))));

        foreach (byte command in new[] { RegisterMentor, RegisterMentee }) {
            handler.Handle(second.Session, TestGameSession.Packet(writer => writer.WriteByte(command)));
            Assert.That(Mentor(second), Is.EqualTo(new[] { Error(command == RegisterMentee ? MentorError.IsNotReturnUser : MentorError.InvalidTarget) }));
        }
        Assert.That(second.Session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.UnregisteredMentor));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetAccountEventValues(session.AccountId).Single(value => value.Type == GameEventUserValueType.MentoringRegistration).Value,
            Is.EqualTo($"5,{session.CharacterId}"));
    }

    // After a mentee registration the account's others get role 3 and Mentor 12 with the mentee second; the
    // registered character keeps its role at its next login from the account's value, whatever its stored role.
    [Test]
    public void Register_AMenteeGivesTheOthersRole3AndKeepsItsRole() {
        Login(season: true);
        Send(session, RegisterMentee);

        TestGameSession second = SameAccount();
        Assert.That(second.Session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.UnregisteredMentee));
        Assert.That(Mentor(second), Has.Member(Hex(MentorPacket.AccountCharacters(0, session.CharacterId))));

        session.Player.Value.Character.MentorRole = MentorRole.MenteeCandidate;
        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
    }

    // The registration is stored before the role is given: a store the database refuses gets the refusal's notice,
    // gives no role and leaves the account free.
    [Test]
    public void Register_AStoreRefusedGivesNoRole() {
        Login(season: true);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.AccountEventValue>().Any();
        Send(session, RegisterMentor);
        test.FailSave = null;

        Assert.That(Mentor(test), Is.EqualTo(new[] { Error(MentorError.InvalidTarget) }));
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MentorCandidate));
        Assert.That(test.World.PlayerUpdates.Any(update => update.HasMentorRole), Is.False);
        Assert.That(SameAccount().Session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.MentorCandidate));
    }

    // A pair and a request of another season are not listed and do not count in this one: the mentee who had a
    // mentor in another season can be invited again in season 1.
    [Test]
    public void Rows_OfAnotherSeasonAreNotRead() {
        MentorAndMentee();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.AddMentoringRequest(session.CharacterId, other.CharacterId, 2, 3, 100, 20, out MentoringEntry? old), Is.Null);
            Assert.That(db.AcceptMentoring(old!.Id, other.CharacterId, 2, 3, out _), Is.Null);
            Assert.That(db.GetMentoring(other.CharacterId, 1), Is.Empty);
        }
        other.Mentoring.Load();
        Assert.That(Mentor(otherTest), Has.Member("0300000000"));

        InviteOther();
        Assert.That(Mentor(test), Is.EqualTo(new[] { Hex(MentorPacket.MenteeInvite(other.CharacterId, other.PlayerName)) }));
        Assert.That(Rows().Single().Accepted, Is.False);
    }

    // Mentoring stored before rows and registrations had a season (AddMentoringSeason): a season-0 pair and each
    // account's registration under event 0, as the migration leaves them.
    private long Unseasoned() {
        using GameStorage.Request db = test.Storage.Context();
        db.AddMentoringRequest(session.CharacterId, other.CharacterId, 0, 3, 100, 20, out MentoringEntry? request);
        db.AcceptMentoring(request!.Id, other.CharacterId, 0, 3, out _);
        foreach ((long accountId, string value) in new[] { (session.AccountId, $"5,{session.CharacterId}"), (other.AccountId, $"2,{other.CharacterId}") }) {
            db.SaveAccountEventValues(accountId, [new GameEventUserValue(value) {
                Type = GameEventUserValueType.MentoringRegistration, EventId = 0, ExpirationTime = long.MaxValue,
            }]);
        }
        return request.Id;
    }

    private bool HasUnseasonedRegistration(GameSession of) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAccountEventValues(of.AccountId).Any(value => value.EventId == 0 && value.Type == GameEventUserValueType.MentoringRegistration);
    }

    // An upgrade during a season keeps the pairs and registrations: each login moves its rows and its account's
    // registration to the running season.
    [Test]
    public void Load_DuringASeasonMovesWhatWasStoredBeforeSeasons() {
        Login(season: true);
        long id = Unseasoned();

        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentor));
        Assert.That(Rows(), Is.EqualTo(new[] { new MentoringEntry(id, session.CharacterId, other.CharacterId, true) }));
        Assert.That(HasUnseasonedRegistration(session), Is.False);
        Assert.That(HasUnseasonedRegistration(other), Is.True);

        other.Mentoring.Load();
        Assert.That(other.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
        Assert.That(HasUnseasonedRegistration(other), Is.False);
        // The pair is the season's: the mentee's mentor coins reach the mentor.
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.AddMentorCoins(other.CharacterId, 1, 100, out long mentorId), Is.True);
        Assert.That(mentorId, Is.EqualTo(session.CharacterId));
    }

    // Without a season they end: the season they were made in is over, and a later season does not take them.
    [Test]
    public void Load_WithoutASeasonEndsWhatWasStoredBeforeSeasons() {
        Login(season: false);
        Unseasoned();

        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.None));
        Assert.That(HasUnseasonedRegistration(session), Is.False);
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetMentoring(session.CharacterId, 0), Is.Empty);
        Assert.That(db.MoveUnseasonedMentoring(other.CharacterId, 1), Is.True);
        Assert.That(db.GetMentoring(other.CharacterId, 1), Is.Empty);
    }

    // The old registration (an account value of event 0) is not loaded with the event values: the login that ends it
    // does not store it again at logout, and it is gone at the next login.
    [Test]
    public void Load_WithoutASeasonTheOldRegistrationIsNotStoredAgain() {
        Login(season: false);
        Unseasoned();

        // The login's event values are read before mentoring (GameSession), as a new manager reads them.
        session.GameEvent = new GameEventManager(session);
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.MentoringRegistration, 0), Is.Null);
        session.Mentoring.Load();
        using (GameStorage.Request db = test.Storage.Context()) {
            session.GameEvent.Save(db);
        }
        Assert.That(HasUnseasonedRegistration(session), Is.False);
        session.GameEvent = new GameEventManager(session);
        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.None));
        Assert.That(HasUnseasonedRegistration(session), Is.False);
    }

    // An account that registered for the season already keeps that registration; the old one is dropped.
    [Test]
    public void Load_KeepsTheSeasonsRegistrationOverTheOldOne() {
        Login(season: true);
        Send(session, RegisterMentee);
        Unseasoned();
        Mentor(test);

        session.Mentoring.Load();
        Assert.That(session.Player.Value.Character.MentorRole, Is.EqualTo(MentorRole.Mentee));
        Assert.That(HasUnseasonedRegistration(session), Is.False);
    }
}

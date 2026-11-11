using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The guild join requests through GameStorage. Each call uses its own request, as the servers do.
// The database is in memory, where the batch deletes cannot run: the tests that delete are skipped unless
// MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database and MS2_TEST_ACCOUNT_ID the
// id of an account in it.
public class GuildApplicationStorageTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private static readonly long AccountId =
        long.TryParse(Environment.GetEnvironmentVariable("MS2_TEST_ACCOUNT_ID"), out long id) ? id : 1;

    private GameStorage storage = null!;
    private string suffix = "";
    private long guildA;
    private long guildB;
    private long applicant;
    private long other;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = Connection == null
            ? new DbContextOptionsBuilder()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options
            : new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        suffix = Guid.NewGuid().ToString("N")[..6];
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        guildA = CreateGuild("GuildA", CreateCharacter("LeaderA"));
        guildB = CreateGuild("GuildB", CreateCharacter("LeaderB"));
        applicant = CreateCharacter("Applicant");
        other = CreateCharacter("Other");
    }

    private long CreateCharacter(string name) {
        using GameStorage.Request db = storage.Context();
        var character = new Character { AccountId = AccountId, Name = name + suffix, Mastery = new Mastery() };
        character.ReturnMaps.Push(2000062);
        Character? created = db.CreateCharacter(character);
        Assert.That(created, Is.Not.Null);
        return created!.Id;
    }

    private long CreateGuild(string name, long leaderId) {
        using GameStorage.Request db = storage.Context();
        Guild? guild = db.CreateGuild(name + suffix, leaderId);
        Assert.That(guild, Is.Not.Null);
        return guild!.Id;
    }

    private static void NeedsBatchDelete() {
        if (Connection == null) {
            Assert.Ignore("batch delete needs a real database: set MS2_TEST_GAME_DB");
        }
    }

    private GuildApplicationRow Apply(long guildId, long characterId) {
        using GameStorage.Request db = storage.Context();
        GuildApplicationRow? row = db.CreateGuildApplication(guildId, characterId);
        Assert.That(row, Is.Not.Null);
        return row!;
    }

    [Test]
    public void Create_IsListedForTheGuildAndForTheApplicant() {
        GuildApplicationRow first = Apply(guildA, applicant);
        GuildApplicationRow second = Apply(guildB, applicant);
        Apply(guildA, other);

        using GameStorage.Request db = storage.Context();
        Assert.That(db.GetGuildApplications(guildA).Select(row => row.ApplicantId), Is.EqualTo(new[] { applicant, other }));
        Assert.That(db.GetGuildApplicationsOf(applicant).Select(row => row.Id), Is.EqualTo(new[] { first.Id, second.Id }));
        Assert.That(db.GetGuildApplication(guildB, applicant)?.Id, Is.EqualTo(second.Id));
        Assert.That(db.GetGuildApplication(guildB, other), Is.Null);
    }

    [Test]
    public void Cancel_FindsTheOwnRowByGuildOrByRequestAndDeletesIt() {
        NeedsBatchDelete();
        GuildApplicationRow first = Apply(guildA, applicant);
        GuildApplicationRow second = Apply(guildB, applicant);
        GuildApplicationRow foreign = Apply(guildB, other);

        IList<GuildApplicationRow> own;
        using (GameStorage.Request db = storage.Context()) {
            own = db.GetGuildApplicationsOf(applicant);
        }
        Assert.That(GuildRules.FindOwnApplication(own, guildB), Is.EqualTo(second));
        Assert.That(GuildRules.FindOwnApplication(own, foreign.Id), Is.Null, "another player's request is never found");

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteGuildApplication(second.Id), Is.True);
            Assert.That(db.DeleteGuildApplication(second.Id), Is.False);
        }

        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetGuildApplicationsOf(applicant).Select(row => row.Id), Is.EqualTo(new[] { first.Id }));
        Assert.That(check.GetGuildApplications(guildB).Select(row => row.Id), Is.EqualTo(new[] { foreign.Id }));
    }

    [Test]
    public void Join_ClearsAllRequestsOfTheNewMember() {
        Apply(guildA, applicant);
        Apply(guildB, applicant);
        Apply(guildB, other);

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetGuildId(applicant), Is.EqualTo(0));
            var info = new PlayerInfo(new CharacterInfo(AccountId, applicant, "Applicant" + suffix, "", "", Maple2.Model.Enum.Gender.Male, Maple2.Model.Enum.Job.Newbie, 1), "", new AchievementInfo(), []);
            // The member row and the removal of the requests are one save.
            Assert.That(db.CreateGuildMember(guildA, info), Is.Not.Null);
        }

        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetGuildId(applicant), Is.EqualTo(guildA));
        Assert.That(check.GetGuildMemberCount(guildA), Is.EqualTo(2));
        Assert.That(check.GetGuildApplicationsOf(applicant), Is.Empty);
        Assert.That(check.GetGuildApplications(guildB), Has.Count.EqualTo(1));
    }

    [Test]
    public void Create_ClearsTheRequestsOfTheNewMaster() {
        Apply(guildA, applicant);
        Apply(guildB, applicant);
        Apply(guildB, other);

        long created = CreateGuild("GuildC", applicant);

        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetGuildId(applicant), Is.EqualTo(created));
        Assert.That(check.GetGuildApplicationsOf(applicant), Is.Empty);
        Assert.That(check.GetGuildApplications(guildA), Is.Empty);
        Assert.That(check.GetGuildApplications(guildB).Select(row => row.ApplicantId), Is.EqualTo(new[] { other }));
    }

    [Test]
    public void Disband_DropsTheRequestsOfTheGuild() {
        NeedsBatchDelete();
        Apply(guildA, applicant);
        Apply(guildB, applicant);

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteGuild(guildA), Is.True);
        }

        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetGuildApplicationsOf(applicant).Select(row => row.GuildId), Is.EqualTo(new[] { guildB }));
    }
}

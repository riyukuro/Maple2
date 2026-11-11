using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The guild search through GameStorage, on an in-memory database: three guilds made the way the server
// makes them, then the listed flag and the focus saved the way the World server saves them.
public class GuildSearchStorageTests {
    private GameStorage storage = null!;
    private string tag = "";
    private long alpha;
    private long beta;
    private long hidden;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        tag = Guid.NewGuid().ToString("N")[..6];

        alpha = Create("Alpha", (GuildFocus) 1, listed: true, experience: 100);
        beta = Create("BetaTeam", (GuildFocus) 6, listed: true, experience: 200);
        hidden = Create("BetaHidden", (GuildFocus) 6, listed: false, experience: 300);
    }

    private long Create(string name, GuildFocus focus, bool listed, int experience) {
        Guild? guild;
        long leaderId;
        using (GameStorage.Request db = storage.Context()) {
            var leader = new Character { AccountId = 1, Name = tag + name, Mastery = new Mastery() };
            leader.ReturnMaps.Push(2000062);
            Character? character = db.CreateCharacter(leader);
            Assert.That(character, Is.Not.Null);
            leaderId = character!.Id;

            guild = db.CreateGuild(tag + name, character!.Id);
            Assert.That(guild, Is.Not.Null);
            Assert.That(guild!.Listed, Is.True, "a new guild is listed");
        }

        // the World server holds the guild with its members; the save writes both
        var info = new PlayerInfo(new CharacterInfo(1, leaderId, tag + name, "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);
        guild.Members.TryAdd(leaderId, new GuildMember { GuildId = guild.Id, Info = info, Rank = 0 });
        guild.Focus = focus;
        guild.Listed = listed;
        guild.Experience = experience;
        using GameStorage.Request save = storage.Context();
        Assert.That(save.SaveGuild(guild), Is.True);
        return guild.Id;
    }

    private long[] Search(int focusMask, string name, int limit = 90) {
        using GameStorage.Request db = storage.Context();
        return db.SearchGuilds(focusMask, name, limit).Select(entry => entry.Id).ToArray();
    }

    [Test]
    public void ListedFlagAndFocus_AreSavedAndLoaded() {
        using GameStorage.Request db = storage.Context();
        Guild? guild = db.GetGuild(hidden);
        Assert.That(guild, Is.Not.Null);
        Assert.That(guild!.Listed, Is.False);
        Assert.That((int) guild.Focus, Is.EqualTo(6));
        Assert.That(db.GetGuild(alpha)!.Listed, Is.True);
    }

    [Test]
    public void OnlyListedGuildsAreFound() {
        Assert.That(Search(0, tag), Is.EqualTo(new[] { alpha, beta }));
    }

    [Test]
    public void FocusSearch_NeedsOneSharedBit() {
        Assert.That(Search(1, tag), Is.EqualTo(new[] { alpha }));
        Assert.That(Search(2, tag), Is.EqualTo(new[] { beta }));
        Assert.That(Search(3, tag), Is.EqualTo(new[] { alpha, beta }));
        Assert.That(Search(8, tag), Is.Empty);
    }

    [Test]
    public void NameSearch_FindsNamesThatHoldTheText() {
        Assert.That(Search(0, tag + "Beta"), Is.EqualTo(new[] { beta }));
        Assert.That(Search(0, tag + "Al"), Is.EqualTo(new[] { alpha }));
        Assert.That(Search(0, tag + "none"), Is.Empty);
    }

    [Test]
    public void LimitCutsTheList() {
        Assert.That(Search(0, tag, limit: 1), Is.EqualTo(new[] { alpha }));
    }

    [Test]
    public void EntryCarriesLeaderMembersAndExperience() {
        using GameStorage.Request db = storage.Context();
        IList<GuildSearchEntry> found = db.SearchGuilds(2, tag, 90);
        Assert.That(found, Has.Count.EqualTo(1));
        GuildSearchEntry entry = found[0];
        Assert.That(entry.Name, Is.EqualTo(tag + "BetaTeam"));
        Assert.That(entry.MemberCount, Is.EqualTo(1));
        Assert.That(entry.Experience, Is.EqualTo(200));
        Assert.That(entry.LeaderName, Is.EqualTo(tag + "BetaTeam"));
        Assert.That(entry.LeaderAccountId, Is.EqualTo(1));
        Assert.That(entry.LeaderCharacterId, Is.GreaterThan(0));
    }
}

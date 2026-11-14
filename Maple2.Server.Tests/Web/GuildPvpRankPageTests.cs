using System;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Web;

// The guild window's PvP ranking page: the client opens maview:/Rank/GuildPVP?tp=realtime (UIGuildDialog::OpenPvpRankPage)
// and shows the HTML it gets. The page lists the current season's guilds by score, then fewer losses, with grade.
public class GuildPvpRankPageTests {
    private static readonly DateTime Now = new(2026, 3, 15);
    private static readonly SeasonDataTable.Entry Season = new(3, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1), [1100, 1200]);

    private static GuildPvpRankRow Row(int rank, string name, string leader, int score, int grade) =>
        new(rank, new GuildPvpStanding(rank, name, "", leader, score, 0), grade);

    [Test]
    public void TheRoute_IsTheClientsUrl() {
        RouteAttribute? route = typeof(RankController).GetCustomAttribute<RouteAttribute>();
        HttpGetAttribute? get = typeof(RankController).GetMethod(nameof(RankController.GetGuildPvp))!.GetCustomAttribute<HttpGetAttribute>();
        Assert.Multiple(() => {
            Assert.That(route?.Template, Is.EqualTo("/Rank/"));
            Assert.That(get?.Template, Is.EqualTo("GuildPVP"));
        });
    }

    [Test]
    public void ThePage_ListsTheRowsInOrderWithTheSeason() {
        string page = RankController.GuildPvpPage(Season, [Row(1, "Blue", "Ann", 1250, 2), Row(2, "Red", "Bob", 1000, 0)]);
        Assert.Multiple(() => {
            Assert.That(page, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(page, Does.Contain("Season 3: 2026-03-01 - 2026-04-01"));
            Assert.That(page, Does.Contain("<tr><td class=\"n\">1</td><td>Blue</td><td>Ann</td><td class=\"n\">1250</td><td class=\"n\">2</td></tr>"));
            Assert.That(page.IndexOf("Blue", StringComparison.Ordinal), Is.LessThan(page.IndexOf("Red", StringComparison.Ordinal)));
            Assert.That(page, Does.Not.Contain(RankController.NoGuild));
        });
    }

    [Test]
    public void Names_AreHtmlEncoded() {
        string page = RankController.GuildPvpPage(Season, [Row(1, "<b>A&B</b>", "\"x\"", 1000, 0)]);
        Assert.Multiple(() => {
            Assert.That(page, Does.Contain("&lt;b&gt;A&amp;B&lt;/b&gt;"));
            Assert.That(page, Does.Contain("&quot;x&quot;"));
            Assert.That(page, Does.Not.Contain("<b>A"));
        });
    }

    [Test]
    public void NoSeasonOrNoGuild_SaysSo() {
        Assert.Multiple(() => {
            Assert.That(RankController.GuildPvpPage(null, []), Does.Contain(RankController.NoSeason));
            Assert.That(RankController.GuildPvpPage(null, []), Does.Not.Contain("<table>"));
            Assert.That(RankController.GuildPvpPage(Season, []), Does.Contain(RankController.NoGuild));
        });
    }

    // From the database: the guilds that fought in the current season, ranked; a guild of another season is left out.
    // The standings query reads the JSON pvp column, which the in-memory database cannot, so this one runs only when
    // MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database and MS2_TEST_ACCOUNT_ID the id of
    // an account in it.
    [Test]
    public void FromTheDatabase_TheCurrentSeasonsGuilds() {
        string? connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
        if (connection == null) {
            Assert.Ignore("the standings query needs a real database: set MS2_TEST_GAME_DB");
        }
        long accountId = long.TryParse(Environment.GetEnvironmentVariable("MS2_TEST_ACCOUNT_ID"), out long id) ? id : 1;
        DbContextOptions options = new DbContextOptionsBuilder().UseMySql(connection, ServerVersion.AutoDetect(connection)).Options;
        var storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        string tag = Guid.NewGuid().ToString("N")[..6];
        // A season id no other guild of the database fought in.
        int seasonId = 900000 + Random.Shared.Next(99999);
        var season = Season with { Id = seasonId };
        AddGuild(storage, accountId, "Low" + tag, "Ann" + tag, new GuildPvp { SeasonId = seasonId, Score = 1010, Wins = 1 });
        AddGuild(storage, accountId, "High" + tag, "Bob" + tag, new GuildPvp { SeasonId = seasonId, Score = 1210, Wins = 3 });
        AddGuild(storage, accountId, "Old" + tag, "Cat" + tag, new GuildPvp { SeasonId = seasonId - 1, Score = 1500, Wins = 9 });
        var seasons = new Dictionary<int, SeasonDataTable.Entry> { [seasonId] = season };

        string page = RankController.GuildPvpPage(storage, seasons, Now);
        Assert.Multiple(() => {
            Assert.That(page, Does.Contain($"<tr><td class=\"n\">1</td><td>High{tag}</td><td>Bob{tag}</td><td class=\"n\">1210</td><td class=\"n\">2</td></tr>"));
            Assert.That(page, Does.Contain($"<tr><td class=\"n\">2</td><td>Low{tag}</td><td>Ann{tag}</td><td class=\"n\">1010</td><td class=\"n\">0</td></tr>"));
            Assert.That(page, Does.Not.Contain("Old" + tag));
        });
        Assert.That(RankController.GuildPvpPage(storage, seasons, new DateTime(2026, 5, 1)), Does.Contain(RankController.NoSeason));
    }

    private static void AddGuild(GameStorage storage, long accountId, string name, string leader, GuildPvp pvp) {
        Guild? guild;
        long leaderId;
        using (GameStorage.Request db = storage.Context()) {
            var character = new Character { AccountId = accountId, Name = leader, Mastery = new Mastery() };
            character.ReturnMaps.Push(2000062);
            Character? created = db.CreateCharacter(character);
            Assert.That(created, Is.Not.Null);
            leaderId = created!.Id;
            guild = db.CreateGuild(name, leaderId);
            Assert.That(guild, Is.Not.Null);
        }
        // The World holds the guild with its members and saves both with its own request, as here.
        var info = new PlayerInfo(new CharacterInfo(accountId, leaderId, leader, "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);
        guild!.Members.TryAdd(leaderId, new GuildMember { GuildId = guild.Id, Info = info, Rank = 0 });
        guild.Pvp = pvp;
        using GameStorage.Request save = storage.Context();
        Assert.That(save.SaveGuild(guild), Is.True);
    }
}

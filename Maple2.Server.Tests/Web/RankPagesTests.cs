using System;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Web;

// The ranking pages the KR client opens in its browser besides the guild championship one: /Rank/Guild (the guild
// window and the town guild boards), /Rank/Character, /Rank/PVP (the rank duel dialog), /rank/darkstream (the dark
// stream result window) and /Rank/Architect (the town architect boards). Each is plain HTML from the in-game board.
public class RankPagesTests {
    private static AchievementInfo Trophy(int combat, int adventure, int lifestyle) =>
        new() { Combat = combat, Adventure = adventure, Lifestyle = lifestyle };

    [TestCase(nameof(RankController.GetGuild), "Guild")]
    [TestCase(nameof(RankController.GetCharacter), "Character")]
    [TestCase(nameof(RankController.GetPvp), "PVP")]
    [TestCase(nameof(RankController.GetDarkStream), "DarkStream")]
    [TestCase(nameof(RankController.GetArchitect), "Architect")]
    public void TheRoute_IsTheClientsUrl(string method, string template) {
        HttpGetAttribute? get = typeof(RankController).GetMethod(method)!.GetCustomAttribute<HttpGetAttribute>();
        Assert.Multiple(() => {
            Assert.That(typeof(RankController).GetCustomAttribute<RouteAttribute>()?.Template, Is.EqualTo("/Rank/"));
            Assert.That(get?.Template, Is.EqualTo(template));
        });
    }

    // Every page an interact object may open (InteractRules.ServedPages) is a route of the web server.
    [Test]
    public void EveryPageObjectsOpen_IsARoute() {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Type controller in new[] { typeof(RankController), typeof(GameController) }) {
            string root = controller.GetCustomAttribute<RouteAttribute>()!.Template;
            foreach (MethodInfo method in controller.GetMethods()) {
                if (method.GetCustomAttribute<HttpGetAttribute>()?.Template is string template) {
                    routes.Add(root + template);
                }
            }
        }
        Assert.That(InteractRules.ServedPages, Is.SubsetOf(routes).Using((IEqualityComparer<string>) StringComparer.OrdinalIgnoreCase));
    }

    [Test]
    public void TheGuildPage_ListsTheGuildsByTrophies() {
        string page = RankController.GuildPage([
            new GuildTrophyRankInfo(1, 7, "Blue", "", "Ann", Trophy(10, 20, 30)),
            new GuildTrophyRankInfo(2, 8, "Red", "", "Bob", Trophy(1, 2, 3)),
        ]);
        Assert.Multiple(() => {
            Assert.That(page, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(page, Does.Contain("<th>Rank</th><th>Guild</th><th>Leader</th><th>Trophies</th>"));
            Assert.That(page, Does.Contain("<tr><td class=\"n\">1</td><td>Blue</td><td>Ann</td><td class=\"n\">60</td>"
                + "<td class=\"n\">10</td><td class=\"n\">20</td><td class=\"n\">30</td></tr>"));
            Assert.That(page.IndexOf("Blue", StringComparison.Ordinal), Is.LessThan(page.IndexOf("Red", StringComparison.Ordinal)));
            Assert.That(page, Does.Not.Contain(RankController.NoGuildTrophy));
        });
    }

    [Test]
    public void TheCharacterPage_ListsTheCharactersByTrophies() {
        string page = RankController.CharacterPage([new TrophyRankInfo(1, 5, "Ann", "", Trophy(4, 5, 6))]);
        Assert.That(page, Does.Contain("<tr><td class=\"n\">1</td><td>Ann</td><td class=\"n\">15</td>"
            + "<td class=\"n\">4</td><td class=\"n\">5</td><td class=\"n\">6</td></tr>"));
    }

    [Test]
    public void ThePvpPage_ListsTheSeasonsDuelists() {
        string page = RankController.PvpPage(4, [new RankDuelRankInfo(1, 5, "Ann", "", 1520, 1600)]);
        Assert.Multiple(() => {
            Assert.That(page, Does.Contain("<p>Season 4</p>"));
            Assert.That(page, Does.Contain("<tr><td class=\"n\">1</td><td>Ann</td><td class=\"n\">1520</td><td class=\"n\">1600</td></tr>"));
        });
    }

    [Test]
    public void TheDarkStreamPage_ListsTheScoresWithTheirDate() {
        long setTime = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        string date = DateTimeOffset.FromUnixTimeSeconds(setTime).ToLocalTime().ToString("yyyy-MM-dd");
        string page = RankController.DarkStreamPage([new DarkStreamRankInfo(1, 5, "Ann", "", 9000, setTime)]);
        Assert.That(page, Does.Contain($"<tr><td class=\"n\">1</td><td>Ann</td><td class=\"n\">9000</td><td>{date}</td></tr>"));
    }

    [Test]
    public void TheArchitectPage_ListsTheHomesOfTheMonth() {
        string page = RankController.ArchitectPage(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            [new HomeCommendRankInfo(1, 5, "Ann", "", 12, 9, "Sunny Home")]);
        Assert.Multiple(() => {
            Assert.That(page, Does.Contain("<p>2026-03</p>"));
            Assert.That(page, Does.Contain("<tr><td class=\"n\">1</td><td>Sunny Home</td><td>Ann</td><td class=\"n\">12</td></tr>"));
        });
    }

    [Test]
    public void Names_AreHtmlEncoded() {
        Assert.Multiple(() => {
            Assert.That(RankController.GuildPage([new GuildTrophyRankInfo(1, 7, "<b>A&B</b>", "", "\"x\"", Trophy(1, 0, 0))]),
                Does.Contain("<td>&lt;b&gt;A&amp;B&lt;/b&gt;</td><td>&quot;x&quot;</td>"));
            Assert.That(RankController.CharacterPage([new TrophyRankInfo(1, 5, "<i>", "", Trophy(1, 0, 0))]), Does.Not.Contain("<i>"));
            Assert.That(RankController.PvpPage(1, [new RankDuelRankInfo(1, 5, "<i>", "", 0, 0)]), Does.Not.Contain("<i>"));
            Assert.That(RankController.DarkStreamPage([new DarkStreamRankInfo(1, 5, "<i>", "", 0, 0)]), Does.Not.Contain("<i>"));
            Assert.That(RankController.ArchitectPage(DateTimeOffset.UnixEpoch, [new HomeCommendRankInfo(1, 5, "<i>", "", 1, 9, "<s>")]),
                Does.Not.Contain("<i>").And.Not.Contain("<s>"));
        });
    }

    [Test]
    public void AnEmptyBoard_SaysSo() {
        Assert.Multiple(() => {
            Assert.That(RankController.GuildPage([]), Does.Contain(RankController.NoGuildTrophy).And.Not.Contain("<table>"));
            Assert.That(RankController.CharacterPage([]), Does.Contain(RankController.NoTrophy).And.Not.Contain("<table>"));
            Assert.That(RankController.PvpPage(4, []), Does.Contain(RankController.NoDuel).And.Not.Contain("<table>"));
            Assert.That(RankController.PvpPage(0, []), Does.Contain(RankController.NoPvpSeason).And.Not.Contain(RankController.NoDuel));
            Assert.That(RankController.DarkStreamPage([]), Does.Contain(RankController.NoDarkStream).And.Not.Contain("<table>"));
            Assert.That(RankController.ArchitectPage(DateTimeOffset.UnixEpoch, []), Does.Contain(RankController.NoCommend));
        });
    }

    // The pages from a real database: each board's query runs and the answer is an HTML page. Runs only when
    // MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database (the boards read JSON columns the
    // in-memory database cannot).
    [Test]
    public void FromTheDatabase_EachPageAnswersHtml() {
        string? connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
        if (connection == null) {
            Assert.Ignore("the boards need a real database: set MS2_TEST_GAME_DB");
        }
        DbContextOptions options = new DbContextOptionsBuilder().UseMySql(connection, ServerVersion.AutoDetect(connection)).Options;
        var storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        var controller = new RankController(storage, null!, new MemoryCache(new MemoryCacheOptions()));
        Assert.Multiple(() => {
            foreach (Func<Microsoft.AspNetCore.Http.IResult> get in new Func<Microsoft.AspNetCore.Http.IResult>[] {
                         controller.GetGuild, controller.GetCharacter, controller.GetDarkStream, controller.GetArchitect,
                     }) {
                var content = get() as ContentHttpResult;
                Assert.That(content?.ContentType, Is.EqualTo("text/html; charset=utf-8"));
                Assert.That(content?.ResponseContent, Does.StartWith("<!DOCTYPE html>").And.EndWith("</body></html>"));
            }
        });
    }

    // rv398: requests that arrive together when a page has expired build it once; the others take that page.
    [Test]
    public void APageAskedByManyAtOnce_IsBuiltOnce() {
        var cache = new MemoryCache(new MemoryCacheOptions());
        int builds = 0;
        using var start = new System.Threading.ManualResetEventSlim(false);
        var tasks = new System.Threading.Tasks.Task<string>[16];
        for (int i = 0; i < tasks.Length; i++) {
            tasks[i] = System.Threading.Tasks.Task.Factory.StartNew(() => {
                start.Wait();
                return RankController.CachedPage(cache, "Test_Once", () => {
                    System.Threading.Interlocked.Increment(ref builds);
                    System.Threading.Thread.Sleep(200);
                    return "page";
                }, TimeSpan.FromMinutes(1));
            }, System.Threading.Tasks.TaskCreationOptions.LongRunning);
        }
        start.Set();
        System.Threading.Tasks.Task.WaitAll(tasks);

        Assert.Multiple(() => {
            Assert.That(builds, Is.EqualTo(1));
            Assert.That(tasks, Has.All.Matches<System.Threading.Tasks.Task<string>>(t => t.Result == "page"));
        });
    }

    [Test]
    public void AnExpiredPage_IsBuiltAgain() {
        var cache = new MemoryCache(new MemoryCacheOptions());
        int builds = 0;
        string Build() => $"page{++builds}";

        Assert.That(RankController.CachedPage(cache, "Test_Expiry", Build, TimeSpan.FromMilliseconds(50)), Is.EqualTo("page1"));
        Assert.That(RankController.CachedPage(cache, "Test_Expiry", Build, TimeSpan.FromMilliseconds(50)), Is.EqualTo("page1"));
        System.Threading.Thread.Sleep(120);
        Assert.That(RankController.CachedPage(cache, "Test_Expiry", Build, TimeSpan.FromMilliseconds(50)), Is.EqualTo("page2"));
    }
}

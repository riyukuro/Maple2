using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Web;

// The ranking pages' actions with a store (an in-memory database, no rows): each answers its page as HTML, and a page
// is kept so the next request does not query again.
public class RankControllerStorageTests {
    private static (RankController Controller, MemoryCache Cache) Controller() {
        var test = new TestGameSession();
        Dictionary<int, SeasonDataTable.Entry> none = [];
        test.SetTable(new SeasonDataTable(none, none, none, none, none, none, none, none, none));
        DbContextOptions options = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new RankController(storage, test.TableMetadata, cache), cache);
    }

    private static string Page(IResult result) {
        var content = (ContentHttpResult) result;
        Assert.That(content.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        return content.ResponseContent!;
    }

    [TestCase("Guild")]
    [TestCase("Character")]
    [TestCase("DarkStream")]
    public void TrophyAndDarkStreamPages_AreHtmlAndKept(string name) {
        (RankController controller, MemoryCache cache) = Controller();
        IResult first = name switch {
            "Guild" => controller.GetGuild(),
            "Character" => controller.GetCharacter(),
            _ => controller.GetDarkStream(),
        };
        string page = Page(first);
        Assert.That(page, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(cache.TryGetValue($"RankPage_{name}", out string? kept), Is.True);
        Assert.That(kept, Is.EqualTo(page));
    }

    [Test]
    public void ArchitectPage_IsHtml() {
        (RankController controller, _) = Controller();
        Assert.That(Page(controller.GetArchitect()), Does.StartWith("<!DOCTYPE html>"));
    }
}

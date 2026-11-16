using System;
using System.Collections.Generic;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.Tests.Web;

// The ranking pages' actions on the paths that need no database: with no season running, /Rank/GuildPVP and /Rank/PVP
// answer an HTML page without a board, and /Rank/PVP keeps its page for the next request.
public class RankControllerRouteTests {
    private static RankController Controller(IMemoryCache cache) {
        var test = new TestGameSession();
        Dictionary<int, SeasonDataTable.Entry> none = [];
        test.SetTable(new SeasonDataTable(none, none, none, none, none, none, none, none, none));
        return new RankController(null!, test.TableMetadata, cache);
    }

    private static (string Body, string? ContentType) Run(IResult result) {
        var content = (ContentHttpResult) result;
        return (content.ResponseContent!, content.ContentType);
    }

    [Test]
    public void GuildPvp_NoSeasonSaysSo() {
        RankController controller = Controller(new MemoryCache(new MemoryCacheOptions()));
        (string body, string? type) = Run(controller.GetGuildPvp());
        Assert.That(type, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(body, Does.Contain(RankController.NoSeason));
    }

    [Test]
    public void Pvp_NoSeasonIsAnEmptyPageKeptInTheCache() {
        var cache = new MemoryCache(new MemoryCacheOptions());
        RankController controller = Controller(cache);
        (string body, string? type) = Run(controller.GetPvp());
        Assert.That(type, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(body, Is.EqualTo(RankController.PvpPage(0, [])));
        Assert.That(cache.TryGetValue("RankPage_PVP_0", out string? kept), Is.True);
        Assert.That(kept, Is.EqualTo(body));
        (string again, _) = Run(controller.GetPvp());
        Assert.That(again, Is.EqualTo(body));
    }
}

using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers;

// The web server's root: the health check (200, no body) for a caller that does not ask for html, and the in-game
// browser's maview page (MaviewPages) for one that does.
[Route("/")]
public class HealthCheckController : ControllerBase {

    [HttpGet("")]
    public IResult Get() {
        if (MaviewPages.WantsHtml(Request.Headers.Accept.ToString())) {
            return Results.Content(MaviewPages.Page(Paths.WEB_DATA_DIR, MaviewPages.Home), "text/html; charset=utf-8");
        }
        return Results.Ok();
    }

    // My News of the character info with AdventureBar off (maview:/Main/SearchNews?c=<character id>).
    [HttpGet("Main/SearchNews")]
    public IResult SearchNews([FromQuery] string? c) {
        return Results.Content(MaviewPages.Page(Paths.WEB_DATA_DIR, MaviewPages.News, c ?? ""), "text/html; charset=utf-8");
    }
}

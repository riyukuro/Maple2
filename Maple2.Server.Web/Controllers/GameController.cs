using System.IO;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers;

// maview pages under the maview url of ServerEnter. /Game/Banner is the notice banner page (the KR NoticeDialogUrl
// constant "maview:/Game/Banner", NoticeRules). Chosen here: Data/game/banner.html when the server has one, else a
// plain page that says there is no notice.
[Route("/Game/")]
public class GameController : ControllerBase {
    public const string DefaultBanner = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Notice</title></head>"
        + "<body style=\"margin:0;padding:16px;font-family:sans-serif;background:#fff;color:#333\">"
        + "<h3 style=\"margin:0 0 8px\">Notice</h3><p style=\"margin:0\">There is no notice.</p></body></html>";

    [HttpGet("Banner")]
    public IResult GetBanner() {
        string fullPath = Path.Combine(Paths.WEB_DATA_DIR, "game", "banner.html");
        if (System.IO.File.Exists(fullPath)) {
            return Results.File(System.IO.File.OpenRead(fullPath), contentType: "text/html; charset=utf-8");
        }
        return Results.Content(DefaultBanner, "text/html; charset=utf-8");
    }
}

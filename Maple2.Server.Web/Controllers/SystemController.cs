using System.IO;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers;

[Route("/system/")]
public class SystemController : ControllerBase {

    [HttpGet("banner/{name}.png")]
    public IResult GetBanner(string name) {
        return WebFiles.Serve(WebFiles.OperatorPath(Paths.WEB_DATA_DIR, name, ".png", "system", "banner"), "image/png");
    }
}

using System.IO;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers.Ugc;

[Route("/blueprint/ms2/01/")]
public class BlueprintController : ControllerBase {

    [HttpGet("{blueprintId}/{ugcUid}.png")]
    public IResult GetBlueprint(long blueprintId, string ugcUid) {
        return WebFiles.Serve(WebFiles.StoredPath(Paths.WEB_DATA_DIR, ugcUid, ".png", "blueprint", blueprintId.ToString()), "image/png");
    }
}

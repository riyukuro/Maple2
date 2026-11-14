using System.IO;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers.Ugc;

[Route("/itemicon/ms2/01/")]
public class ItemIconController : ControllerBase {

    [HttpGet("{itemId}/{uid}.png")]
    public IResult GetItem(long itemId, string uid) {
        return WebFiles.Serve(WebFiles.StoredPath(Paths.WEB_DATA_DIR, uid, ".png", "itemicon", itemId.ToString()), "image/png");
    }
}

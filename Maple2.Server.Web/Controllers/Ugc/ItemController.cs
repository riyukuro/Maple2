using System.IO;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers.Ugc;

[Route("/item/ms2/01/")]
public class ItemController : ControllerBase {

    [HttpGet("{itemId}/{uid}.m2u")]
    public IResult GetItem(long itemId, string uid) {
        return WebFiles.Serve(WebFiles.StoredPath(Paths.WEB_DATA_DIR, uid, ".m2u", "items", itemId.ToString()), "application/octet-stream");
    }
}

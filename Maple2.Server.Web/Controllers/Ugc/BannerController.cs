using System.IO;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers.Ugc;

[Route("/banner/ms2/01/")]
public class BannerController : ControllerBase {

    [HttpGet("{bannerId}/{fileHash}.m2u")]
    public IResult GetBanner(long bannerId, string fileHash) {
        return WebFiles.Serve(WebFiles.StoredPath(Paths.WEB_DATA_DIR, fileHash, ".m2u", "banner", bannerId.ToString()), "application/octet-stream");
    }
}

using System.IO;
using System.Text.RegularExpressions;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers;

// The profile pictures the upload (urq.aspx, ProfileAvatar) stores: it answers the key
// data/profiles/avatar/<character>/<name>.png and writes the file to Data/profiles/<character>/<name>.png. A client
// that shows a portrait (PC profile key, FUN_141028be0 -> CUgcManager::DownloadProfileImage) fetches the key from the
// web endpoint; without this route no uploaded picture could be fetched.
[Route("/data/profiles/avatar/")]
public partial class ProfileController : ControllerBase {
    [GeneratedRegex("^[0-9A-Za-z-]+\\.png$")]
    private static partial Regex FileName();

    [HttpGet("{characterId:long}/{file}")]
    public IResult GetAvatar(long characterId, string file) {
        string? path = AvatarPath(Paths.WEB_DATA_DIR, characterId, file);
        if (path == null || !System.IO.File.Exists(path)) {
            return Results.NotFound();
        }
        return Results.File(System.IO.File.OpenRead(path), contentType: "image/png");
    }

    // The stored file of a key's character and name; null for a name that is not a plain upload name (no paths).
    public static string? AvatarPath(string dataDir, long characterId, string file) {
        if (characterId <= 0 || !FileName().IsMatch(file)) {
            return null;
        }
        return Path.Combine(dataDir, "profiles", characterId.ToString(), file);
    }
}

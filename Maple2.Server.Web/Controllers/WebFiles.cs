using System.IO;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Maple2.Server.Web.Controllers;

// The stored files the GET routes serve. A route's file segment names one file the upload wrote (a resource id, a
// guid, "<id>_icon"), never a path: the segment is decoded before it reaches the action, and on a Windows host a
// decoded backslash ("..%5C") is a folder separator, so any other name is refused before it is joined to the folder.
public static partial class WebFiles {
    [GeneratedRegex("^[0-9A-Za-z_-]{1,64}$")]
    private static partial Regex Name();

    // An operator's file (the system banners: the image URL is whatever the banner row names) may also have dots; a
    // dot is no separator, so the name still stays in its folder. It may not start with one ("." and "..", hidden files).
    [GeneratedRegex("^[0-9A-Za-z_-][0-9A-Za-z_.-]{0,63}$")]
    private static partial Regex OperatorName();

    public static bool PlainName(string? name) => name != null && Name().IsMatch(name);

    public static bool OperatorFileName(string? name) => name != null && OperatorName().IsMatch(name);

    // The file under dataDir/folders.../name+extension, or null for a name that is not a plain name.
    public static string? StoredPath(string dataDir, string name, string extension, params string[] folders) {
        return PlainName(name) ? Join(dataDir, name, extension, folders) : null;
    }

    // The same for an operator's file, whose name may have dots.
    public static string? OperatorPath(string dataDir, string name, string extension, params string[] folders) {
        return OperatorFileName(name) ? Join(dataDir, name, extension, folders) : null;
    }

    private static string Join(string dataDir, string name, string extension, string[] folders) {
        string[] parts = new string[folders.Length + 2];
        parts[0] = dataDir;
        folders.CopyTo(parts, 1);
        parts[^1] = name + extension;
        return Path.Combine(parts);
    }

    public static IResult Serve(string? path, string contentType) {
        if (path == null || !File.Exists(path)) {
            return Results.NotFound();
        }
        return Results.File(File.OpenRead(path), contentType: contentType);
    }
}

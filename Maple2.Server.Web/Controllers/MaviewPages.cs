using System;
using System.IO;

namespace Maple2.Server.Web.Controllers;

// The pages the client's in-game browser (0x4e6a) opens on the web server: the AdventureBar menu entry ("maver") the
// maview base url (ServerEnter, g_strMaviewURL), and the character info's My News, with AdventureBar off,
// maview:/Main/SearchNews?c=<character id> (UIPCInfoDialog::OnClickMyNews). The official pages were Nexon's own site
// and are not known, so the server shows no content of its own: an operator may drop the pages under the web data dir
// (pages/maview.html, pages/news.html, where {c} is replaced with the character id); without the file a neutral empty
// page is answered.
public static class MaviewPages {
    public const string Folder = "pages";
    public const string Home = "maview.html";
    public const string News = "news.html";
    public const string Empty = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body></body></html>";

    // The operator's page under root, or the empty page. characterId replaces {c}; only digits pass.
    public static string Page(string root, string name, string characterId = "") {
        string path = Path.Combine(root, Folder, name);
        string html;
        try {
            html = File.Exists(path) ? File.ReadAllText(path) : Empty;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            html = Empty;
        }
        return html.Replace("{c}", ulong.TryParse(characterId, out ulong id) ? id.ToString() : "");
    }

    // A browser asks for html; the health check's callers (status only) do not.
    public static bool WantsHtml(string? accept) =>
        accept != null && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
}

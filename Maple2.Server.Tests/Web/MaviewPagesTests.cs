using System;
using System.IO;
using System.Reflection;
using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Tests.Web;

// The in-game browser's maview pages: the operator's file when there is one ({c} = the character id, digits only),
// else the empty page; only a caller asking for html gets a page at the root, the health check keeps its 200.
public class MaviewPagesTests {
    [Test]
    public void OperatorPageOrEmpty() {
        string root = Path.Combine(Path.GetTempPath(), "maview-" + Guid.NewGuid());
        try {
            Assert.That(MaviewPages.Page(root, MaviewPages.News, "20000000001"), Is.EqualTo(MaviewPages.Empty));
            Directory.CreateDirectory(Path.Combine(root, MaviewPages.Folder));
            System.IO.File.WriteAllText(Path.Combine(root, MaviewPages.Folder, MaviewPages.News), "<p>news of {c}</p>");
            Assert.That(MaviewPages.Page(root, MaviewPages.News, "20000000001"), Is.EqualTo("<p>news of 20000000001</p>"));
            Assert.That(MaviewPages.Page(root, MaviewPages.News, "1<script>"), Is.EqualTo("<p>news of </p>"));
            Assert.That(MaviewPages.Page(root, MaviewPages.News, ""), Is.EqualTo("<p>news of </p>"));
            Assert.That(MaviewPages.Page(root, MaviewPages.Home), Is.EqualTo(MaviewPages.Empty));
        } finally {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void OnlyHtmlCallersGetAPage() {
        Assert.That(MaviewPages.WantsHtml("text/html, application/xhtml+xml, */*"), Is.True);
        Assert.That(MaviewPages.WantsHtml("*/*"), Is.False);
        Assert.That(MaviewPages.WantsHtml(null), Is.False);
    }

    // My News opens maview:/Main/SearchNews?c=<character id> (UIPCInfoDialog::OnClickMyNews).
    [Test]
    public void SearchNewsRoute() {
        MethodInfo method = typeof(HealthCheckController).GetMethod(nameof(HealthCheckController.SearchNews))!;
        Assert.That(method.GetCustomAttribute<HttpGetAttribute>()!.Template, Is.EqualTo("Main/SearchNews"));
        Assert.That(typeof(HealthCheckController).GetCustomAttribute<RouteAttribute>()!.Template, Is.EqualTo("/"));
    }
}

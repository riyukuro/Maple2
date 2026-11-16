using System.IO;
using Maple2.Server.Web.Controllers;
using Maple2.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Maple2.Server.Tests.Web;

// The banner actions of a server with no operator files: /Game/Banner shows the plain "no notice" page and
// /system/banner/{name}.png is not found.
public class BannerPageActionsTests {
    [Test]
    public void GameBanner_WithoutAnOperatorPageIsTheDefaultNotice() {
        Assume.That(System.IO.File.Exists(Path.Combine(Paths.WEB_DATA_DIR, "game", "banner.html")), Is.False);
        IResult result = new GameController().GetBanner();
        Assert.That(result, Is.TypeOf<ContentHttpResult>());
        var content = (ContentHttpResult) result;
        Assert.That(content.ResponseContent, Is.EqualTo(GameController.DefaultBanner));
        Assert.That(content.ContentType, Is.EqualTo("text/html; charset=utf-8"));
    }

    [Test]
    public void SystemBanner_WithoutAFileIsNotFound() {
        Assert.That(new SystemController().GetBanner("no-such-banner-0"), Is.TypeOf<NotFound>());
    }
}

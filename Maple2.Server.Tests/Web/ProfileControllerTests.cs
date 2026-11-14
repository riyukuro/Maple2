using System.IO;
using System.Linq;
using System.Reflection;
using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maple2.Server.Tests.Web;

// The profile picture route serves the file the upload wrote for the key it answered: the key
// data/profiles/avatar/<character>/<name>.png is the file Data/profiles/<character>/<name>.png; names with a path are
// refused.
public class ProfileControllerTests {
    [Test]
    public void AvatarPath_OfTheUploadKey() {
        Assert.That(ProfileController.AvatarPath("D", 20000000000, "0f8fad5b-d9cb-469f-a165-70867728950e.png"),
            Is.EqualTo(Path.Combine("D", "profiles", "20000000000", "0f8fad5b-d9cb-469f-a165-70867728950e.png")));
        Assert.That(ProfileController.AvatarPath("D", 1, "../x.png"), Is.Null);
        Assert.That(ProfileController.AvatarPath("D", 1, "a.jpg"), Is.Null);
        Assert.That(ProfileController.AvatarPath("D", 0, "a.png"), Is.Null);
    }

    // One route serves the pictures: a second controller of the same path ({hash}.png, no name check) took the requests
    // first, as its segment is the more specific, so the name check above never ran.
    [Test]
    public void OneControllerServesTheProfilePictures() {
        var routes = typeof(ProfileController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods().SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
                .Select(get => ((type.GetCustomAttribute<RouteAttribute>()?.Template ?? "").Trim('/') + "/" + get.Template).ToLowerInvariant()))
            .Where(route => route.Contains("profiles"))
            .ToList();
        Assert.That(routes, Is.EqualTo(new[] { "data/profiles/avatar/{characterid:long}/{file}" }));
    }
}

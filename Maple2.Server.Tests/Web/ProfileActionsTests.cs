using Maple2.Server.Web.Controllers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Maple2.Server.Tests.Web;

// The profile picture actions on a name or character with no stored file: not found, and a name that is no plain
// upload name (a path) is refused the same way without reading anything.
public class ProfileActionsTests {
    [TestCase("nothing-here-0.png")]
    [TestCase("../../etc/passwd")]
    [TestCase("a.jpg")]
    public void AvatarAndWedding_WithoutAFileAreNotFound(string file) {
        var controller = new ProfileController();
        Assert.That(controller.GetAvatar(987654321012345, file), Is.TypeOf<NotFound>());
        Assert.That(controller.GetWeddingProfile(987654321012345, file), Is.TypeOf<NotFound>());
    }
}

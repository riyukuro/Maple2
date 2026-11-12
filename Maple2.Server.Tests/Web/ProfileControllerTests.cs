using System.IO;
using Maple2.Server.Web.Controllers;

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
}

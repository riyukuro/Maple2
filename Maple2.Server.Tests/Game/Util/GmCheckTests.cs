using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class GmCheckTests {
    [Test]
    public void IsGm_PassesGameMasterAndAdmin() {
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.Admin), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster | AdminPermissions.Debug), Is.True);
    }

    [Test]
    public void IsGm_RefusesPlainAndPartialAccounts() {
        Assert.That(GmCheck.IsGm(AdminPermissions.None), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.Warp), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.Debug | AdminPermissions.SpawnItem), Is.False);
    }
}

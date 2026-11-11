using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SeatReleaseEchoTests {
    [Test]
    public void SecondRequestRightAfterTheRelease_IsSwallowedOnce() {
        var echo = new SeatReleaseEcho();
        echo.Released("1_2_3", 1000);

        Assert.That(echo.Swallow("1_2_3", 1040), Is.True);
        Assert.That(echo.Swallow("1_2_3", 1080), Is.False); // the next one is a sit-down
    }

    [Test]
    public void NoRelease_NothingIsSwallowed() {
        Assert.That(new SeatReleaseEcho().Swallow("1_2_3", 1000), Is.False);
    }

    [Test]
    public void AnotherSeat_IsNotSwallowed() {
        var echo = new SeatReleaseEcho();
        echo.Released("1_2_3", 1000);

        Assert.That(echo.Swallow("4_5_6", 1040), Is.False);
        Assert.That(echo.Swallow("1_2_3", 1050), Is.True); // the release of the first seat still stands
    }

    [Test]
    public void AfterTheWindow_TheRequestIsASitDown() {
        var echo = new SeatReleaseEcho();
        echo.Released("1_2_3", 1000);

        Assert.That(echo.Swallow("1_2_3", 1000 + SeatReleaseEcho.WindowMs + 1), Is.False);
        Assert.That(echo.Swallow("1_2_3", 1000 + SeatReleaseEcho.WindowMs + 2), Is.False);
    }

    [Test]
    public void EdgeOfTheWindow_IsSwallowed() {
        var echo = new SeatReleaseEcho();
        echo.Released("1_2_3", 1000);

        Assert.That(echo.Swallow("1_2_3", 1000 + SeatReleaseEcho.WindowMs), Is.True);
    }
}

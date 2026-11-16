using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Tests.Game.Manager;

// Forfeiting and extending an outdoor plot answer a refusal with a ResponseCube error (command 0, then the error
// byte) before the field, the cost or the storage is touched.
public class PlotRefusalTests {
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private static PlotInfo Plot(long expiry, long owner = 1) {
        return new PlotInfo(new UgcMapGroup(1, 0, 0, new UgcMapGroup.Cost(1000, 90000001, 30), new UgcMapGroup.Cost(400, 90000001, 30),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { Id = 5, OwnerId = owner, MapId = 2000062, ExpiryTime = expiry };
    }

    private UgcMapError Refusal() {
        byte[][] sent = test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.ResponseCube).ToArray();
        Assert.That(sent, Has.Length.EqualTo(1));
        return (UgcMapError) sent[0][3];
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Test]
    public void Forfeit_WithoutAPlot_IsNotTheOwner() {
        GameSession session = Start();

        Assert.That(session.Housing.ForfeitPlot(), Is.Null);
        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_dont_have_ownership));
    }

    [Test]
    public void Forfeit_OfAnExpiredPlot_IsRefused() {
        GameSession session = Start();
        session.Housing.SetPlot(Plot(Now - 10));
        test.TakeSent();

        Assert.That(session.Housing.ForfeitPlot(), Is.Null);
        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_not_extension_date));
        Assert.That(session.Player.Value.Home.Outdoor, Is.Not.Null);
    }

    [Test]
    public void Extend_WithoutAPlot_IsNotTheOwner() {
        GameSession session = Start();

        session.Housing.ExtendPlot();
        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_dont_have_ownership));
    }

    [Test]
    public void Extend_OfAnExpiredPlot_IsRefused() {
        GameSession session = Start();
        session.Housing.SetPlot(Plot(Now - 10));
        test.TakeSent();

        session.Housing.ExtendPlot();
        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_expired_salable_group));
    }

    // A plot with more than 30 days left cannot be extended yet.
    [Test]
    public void Extend_WithMoreThanThirtyDaysLeft_IsRefused() {
        GameSession session = Start();
        session.Housing.SetPlot(Plot(Now + 31 * 86400));
        test.TakeSent();

        session.Housing.ExtendPlot();
        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_not_extension_date));
    }

    // Short of the cost: refused, nothing is taken.
    [Test]
    public void Extend_WithoutTheCost_NeedsPayment() {
        GameSession session = Start();
        session.Housing.SetPlot(Plot(Now + 86400));
        test.TakeSent();

        session.Housing.ExtendPlot();
        Assert.That(Refusal(), Is.EqualTo(UgcMapError.s_ugcmap_need_extension_pay));
    }
}

using System;
using System.Collections.Generic;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;

namespace Maple2.Server.Tests.Validators;

public class LiftupRulesTests {
    [Test]
    public void OnlyCubesNotYetBackAreAway() {
        var lifted = new Dictionary<Vector3B, long> {
            [new Vector3B(17, -6, 5)] = 5000,
            [new Vector3B(1, 2, 3)] = 3000,
            [new Vector3B(4, 5, 6)] = 2999,
        };

        var away = LiftupRules.Away(lifted, 3000);

        Assert.That(away, Is.EqualTo(new[] { (new Vector3B(17, -6, 5), 5000) }));
        Assert.That(LiftupRules.IsAway(3000, 3000), Is.False);
        Assert.That(LiftupRules.IsAway(3001, 3000), Is.True);
    }

    [Test]
    public void ClientTickWrapsLikeA32BitTickCount() {
        Assert.That(LiftupRules.ClientTick(0x1_0000_0005L), Is.EqualTo(5));
        Assert.That(LiftupRules.ClientTick(0x1B9BA70BL), Is.EqualTo(0x1B9BA70B));
    }

    // ResponseCube 0x1b: the count, then the cube's cell key and the tick it comes back at (13 bytes for one).
    [Test]
    public void LiftedCubesBytes() {
        byte[] bytes = CubePacket.LiftedCubes([(new Vector3B(17, -6, 5), 0x1B9BA70B)]).ToArray();

        Assert.That(bytes.Length, Is.EqualTo(2 + 13));
        Assert.That(Convert.ToHexString(bytes[2..]), Is.EqualTo("1B0100000011FA05000BA79B1B"));
    }

    // ResponseCube 7: short reason, flag byte, map id, plot number (12 bytes).
    [Test]
    public void ConfirmForfeitPlotBytes() {
        var cost = new UgcMapGroup.Cost(0, 0, 0);
        var plot = new PlotInfo(new UgcMapGroup(10, 0, 0, cost, cost, new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { MapId = 2000051, Number = 10 };

        byte[] owner = CubePacket.ConfirmForfeitPlot(plot, CubePacket.ForfeitByOwner).ToArray();
        byte[] expired = CubePacket.ConfirmForfeitPlot(plot, CubePacket.ForfeitExpired).ToArray();

        Assert.That(Convert.ToHexString(owner[2..]), Is.EqualTo("07720000B3841E000A000000"));
        Assert.That(Convert.ToHexString(expired[2..]), Is.EqualTo("07750000B3841E000A000000"));
    }

    // The World's expiry job names the owner's account with Forfeit set; a channel answers that owner with 0x75.
    // A plot that only opens for sale again (no owner) and a plain plot update send no notice.
    [Test]
    public void ExpiredNoticeFollowsTheExpiryRequest() {
        byte[]? expired = ChannelService.ExpiredNotice(2000051, 10, new FieldPlotRequest.Types.UpdatePlot { AccountId = 7, Forfeit = true })?.ToArray();

        Assert.That(expired, Is.Not.Null);
        Assert.That(Convert.ToHexString(expired![2..]), Is.EqualTo("07750000B3841E000A000000"));
        Assert.That(ChannelService.ExpiredNotice(2000051, 10, new FieldPlotRequest.Types.UpdatePlot { AccountId = 0, Forfeit = false }), Is.Null);
        Assert.That(ChannelService.ExpiredNotice(2000051, 10, new FieldPlotRequest.Types.UpdatePlot { AccountId = 7, Forfeit = false }), Is.Null);
    }
}

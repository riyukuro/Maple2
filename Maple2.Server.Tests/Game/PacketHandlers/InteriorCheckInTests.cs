using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The daily check-in (RequestCube 0x28) takes the reward row the client shows (CMasteryInteriorTable::GetDropBox,
// masteryinteriortable.cpp:359): a home whose decoration score is below every row of a point above 0 has no reward,
// so the client offers no check-in; one sent anyway is refused with 0x28 85 and the day's check-in stays open.
// The box ids are made up.
public class InteriorCheckInTests {
    private TestGameSession test = null!;

    private (GameSession Session, Plot Plot) StartInHome() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry> {
            [0] = new(0, 900001),
            [100] = new(100, 900001),
            [300] = new(300, 900002),
        }));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        test.TakeSent();
        return (session, plot);
    }

    [Test]
    public void AnEmptyHome_HasNoCheckIn() {
        (GameSession session, Plot plot) = StartInHome();
        Assert.That(plot.Cubes, Is.Empty);

        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0x28)));

        byte[] refusal = CubePacket.InteriorCheckInError(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(session.Player.Value.Home.DecorationRewardTimestamp, Is.EqualTo(0));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}

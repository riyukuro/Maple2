using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Entering a field without Limit.Ride: KMS2 sends the rider a Stop with ride-off type DisableField just before its
// own FieldAddUser, laid out as mode 1, int owner id, byte 12, byte 0. Made-up owner id.
public class RideStopPacketTests {
    [Test]
    public void DisableFieldStop_MatchesKms2Layout() {
        byte[] bytes = RidePacket.Stop(4321, RideOffAction.Create(RideOffType.DisableField)).ToArray();
        var reader = new ByteReader(bytes, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseRide));
        Assert.That(bytes.Skip(2).ToArray(), Is.EqualTo(new byte[] { 1, 0xe1, 0x10, 0, 0, 12, 0 }));
    }
}

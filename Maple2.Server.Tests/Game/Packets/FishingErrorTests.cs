using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class FishingErrorTests {
    // Client: mode 2 (client/ms2/fishingmanager.cpp:642) reads a short code and switches on it.
    [TestCase(FishingError.s_fishing_error_notexist_water, 1)]
    [TestCase(FishingError.s_fishing_error_invalid_item, 2)]
    [TestCase(FishingError.s_fishing_error_lack_mastery, 3)]
    [TestCase(FishingError.s_fishing_error_notexist_fish, 4)]
    [TestCase(FishingError.s_fishing_error_invalid_cube, 5)]
    [TestCase(FishingError.s_fishing_error_fishingrod_mastery, 6)]
    [TestCase(FishingError.s_fishing_error_inventory_full, 7)]
    [TestCase(FishingError.s_fishing_error_ugcmap, 8)]
    [TestCase(FishingError.s_fishing_error_system_error, 9)]
    public void ClientCodes(FishingError error, short code) {
        ByteWriter writer = FishingPacket.Error(error);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Fishing));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadShort(), Is.EqualTo(code));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 2));
    }
}

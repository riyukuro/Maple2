using System;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// PlayInstrument mode 1 and Fishing mode 7 with the bytes KMS2 sends.
public class InstrumentFishingPacketTests {
    // A keyboard note of another player: the instrument's object id, the owner's, the MIDI message.
    [Test]
    public void Improvise_IsTheInstrumentTheOwnerAndTheMidiMessage() {
        var instrument = new FieldInstrument(null!, 0x000D2F22, new InstrumentMetadata(1, 0, 0, 0, 0, 0)) {
            OwnerId = 0x0009C060,
        };

        Assert.That(Convert.ToHexString(InstrumentPacket.Improvise(instrument, 0x00646390).ToArray()),
            Is.EqualTo("C700" + "01" + "222F0D00" + "60C00900" + "90636400"));
    }

    // The fish album of a login: the count, then per fish its id, the catches, the prize catches and the
    // largest size. An empty album is the count 0.
    [Test]
    public void LoadAlbum_IsTheCountAndFourIntsPerFish() {
        var fish = new FishEntry(0x00A7D8F6) { TotalCaught = 0x5D, TotalPrizeFish = 1, LargestSize = 0x17D };

        Assert.That(Convert.ToHexString(FishingPacket.LoadAlbum([fish]).ToArray()),
            Is.EqualTo("C400" + "07" + "01000000" + "F6D8A700" + "5D000000" + "01000000" + "7D010000"));
        Assert.That(Convert.ToHexString(FishingPacket.LoadAlbum([]).ToArray()), Is.EqualTo("C400" + "07" + "00000000"));
    }
}

using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: CPartyManager::OnMatchPartyError reads a byte request type and an int result. The "already registered" text is
// shown for 12, 14 and 15; "recruit not found" only with request type 1.
public class PartySearchPacketTests {
    [Test]
    public void AlreadyRegisteredUsesAValueTheClientKnows() {
        ByteWriter writer = PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_already_register);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PartySearch));
            Assert.That(reader.ReadByte(), Is.EqualTo(4));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.AnyOf(12, 14, 15));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4));
        });
    }

    [Test]
    public void NotFoundRecruitCarriesRequestType() {
        ByteWriter writer = PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_not_find_recruit, 1);
        var reader = new ByteReader(writer.Buffer, 0);
        reader.Read<SendOp>();
        reader.ReadByte();
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(106));
        });
    }
}

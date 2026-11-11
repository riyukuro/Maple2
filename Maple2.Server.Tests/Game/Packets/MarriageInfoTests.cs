using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game.Packets;

// The layout is the tail of the client's CPCData::Read (commonfiles/pcdata.cpp:142): short status, 2 names,
// long promise time, long marriage time.
public class MarriageInfoTests {
    [TestCase(MaritalStatus.Single, 0, 0)]
    [TestCase(MaritalStatus.Engaged, 1000, 0)]
    [TestCase(MaritalStatus.Married, 0, 1000)]
    public void WriteTo_WritesWhatTheClientReads(MaritalStatus status, long promiseTime, long marriageTime) {
        var info = new MarriageInfo {
            Status = status,
            CreationTime = 1000,
            MarriageTime = 1000,
            Partner1Name = "ab",
            Partner2Name = "c",
        };
        var writer = new ByteWriter();
        info.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadShort(), Is.EqualTo((short) status));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("c"));
        Assert.That(reader.ReadLong(), Is.EqualTo(promiseTime));
        Assert.That(reader.ReadLong(), Is.EqualTo(marriageTime));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}

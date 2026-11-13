using Maple2.Model.Game.Party;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: CPartyManager::OnMatchParty (client/ms2/partymanager.cpp:1579). The recruit block is CMatchPartyRecruit::Read
// (commonfiles/matchpartyrecruitschcond.cpp:252): i64, i32, i32, i32, str, u8, i32, i32, i64, i64, str, i64.
public class PartySearchLayoutTests {
    private static PartySearch Search(long id) {
        return new PartySearch(id, "ab", 4) {
            PartyId = 7,
            LeaderName = "cd",
            NoApproval = true,
            MemberCount = 2,
            LeaderAccountId = 0x11,
            LeaderCharacterId = 0x22,
            CreationTime = 0x33,
        };
    }

    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PartySearch));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    private static void ReadRecruit(ByteReader reader, long id) {
        Assert.That(reader.ReadLong(), Is.EqualTo(id));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x11));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x22));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("cd"));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x33));
    }

    // OnMatchPartyRecruitRegistered (partymanager.cpp:1641): the recruit block.
    [Test]
    public void AddIsTheRecruitBlock() {
        ByteWriter writer = PartySearchPacket.Add(Search(5));
        ByteReader reader = Open(writer, 0);
        ReadRecruit(reader, 5);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
        Assert.That(writer.Length, Is.EqualTo(3 + 8 + 12 + 6 + 1 + 8 + 16 + 6 + 8));
    }

    // OnMatchPartyRecruitRemoved (partymanager.cpp:1652): i64.
    [Test]
    public void RemoveIsTheId() {
        ByteWriter writer = PartySearchPacket.Remove(0x0102030405060708);
        ByteReader reader = Open(writer, 1);
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // OnMatchPartySearchResults (partymanager.cpp:1664): i32 n, then per entry a u8 (nonzero = a recruit follows).
    [Test]
    public void LoadIsCountThenFlaggedRecruits() {
        ByteWriter writer = PartySearchPacket.Load(new[] { Search(5), Search(6) });
        ByteReader reader = Open(writer, 2);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        ReadRecruit(reader, 5);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        ReadRecruit(reader, 6);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void EmptyLoadIsOnlyTheCount() {
        ByteWriter writer = PartySearchPacket.Load(new PartySearch[0]);
        ByteReader reader = Open(writer, 2);
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}

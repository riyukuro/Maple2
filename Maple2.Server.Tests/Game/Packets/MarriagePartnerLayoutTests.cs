using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Wedding 0 (UpdateMarriage) as WeddingProxy::Read (weddingproxy.cpp:227) reads it. Each partner is
// Unk_WeddingPartner::Read (weddingproxy.cpp:77): character id, account id, name and the online flag.
public class MarriagePartnerLayoutTests {
    private static MarriagePartner Partner(long accountId, long characterId, string name, short channel) {
        var info = new PlayerInfo(new CharacterInfo(accountId, characterId, name, "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);
        info.Channel = channel;
        return new MarriagePartner { Info = info };
    }

    [TestCase((short) 1, true)]
    [TestCase((short) -1, false)]
    public void PartnerFlag_IsOnline(short channel, bool online) {
        var writer = new ByteWriter();
        writer.WriteClass<MarriagePartner>(Partner(4, 3, "b", channel));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
        Assert.That(reader.ReadBool(), Is.EqualTo(online));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void UpdateMarriage_ReadsToTheLastLogField() {
        var marriage = new Marriage {
            Id = 9,
            Status = MaritalStatus.Married,
            MarriageTime = 50,
            Exp = 7,
            Profile = "p",
            Partner1 = Partner(1, 2, "a", 1),
            Partner2 = Partner(4, 3, "b", -1),
            ExpHistory = [new MarriageExp { Type = MarriageExpType.coupleMessage, Amount = 5, Time = 6 }],
        };
        marriage.Partner1.Message = "m1";
        marriage.Partner2.Message = "m2";

        ByteWriter packet = WeddingPacket.UpdateMarriage(marriage);
        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Wedding));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(9));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) MaritalStatus.Married));
        reader.ReadLong(); // state time
        Assert.That(reader.ReadLong(), Is.EqualTo(50));
        Assert.That(reader.ReadLong(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("a"));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadLong(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("m1"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("m2"));
        Assert.That(reader.ReadUnicodeString(), Is.Empty);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("p"));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(6));
        Assert.That(reader.Position, Is.EqualTo(packet.Length));
    }
}

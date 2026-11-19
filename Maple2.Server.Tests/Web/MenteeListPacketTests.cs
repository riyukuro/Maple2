using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Web;

// The ruq.aspx answer as the client reads it (ReadMentor): two ids, name, picture, level, job, online. Both ids are
// the character's: the second is what the right-click menu asks character info, trade and report about.
public class MenteeListPacketTests {
    [Test]
    public void EachRow_CarriesTheCharacterIdInBothIds() {
        var mentee = new PlayerInfo(new CharacterInfo(10000000001, 20000000001, "a", "", "p", Gender.Male, Job.Knight, 30), "", new AchievementInfo(), []);
        ByteWriter writer = MentorPacket.MenteeList([mentee]);

        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(20000000001));
        Assert.That(reader.ReadLong(), Is.EqualTo(20000000001));
        Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo("a"));
        Assert.That(reader.ReadUnicodeStringWithLength(), Is.EqualTo("p"));
        Assert.That(reader.ReadInt(), Is.EqualTo(30));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Knight));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

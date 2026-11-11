using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The info window of another player (CharacterInfo): CPCData::Read (commonfiles/pcdata.cpp:101) and the window's use of
// it (UIPCInfoDialog::FUN_14132c330, uipcinfodialog.cpp:289).
public class PlayerInfoPacketTests {
    // m_n408 then m_n40c; the window shows "{m_n40c} / {m_n408}" in the order of the home info window, this month
    // then all time.
    [Test]
    public void ArchitectScores_AllTimeThenThisMonth() {
        var home = new Home { CurrentArchitectScore = 3, ArchitectScore = 40 };
        var writer = new ByteWriter();
        PlayerInfoPacket.WriteArchitectScores(writer, home);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(40));
            Assert.That(reader.ReadInt(), Is.EqualTo(3));
            Assert.That(writer.Length, Is.EqualTo(8));
        });
    }

    // The rank name after the guild name; none without a guild or a rank.
    [Test]
    public void GuildRankName() {
        Assert.Multiple(() => {
            Assert.That(PlayerInfoPacket.GuildRankName(new GuildRank { Id = 1, Name = "Officer" }), Is.EqualTo("Officer"));
            Assert.That(PlayerInfoPacket.GuildRankName(null), Is.EqualTo(string.Empty));
        });
    }

    // A player not found: the id and false (the client shows s_char_info_err_not_found).
    [Test]
    public void NotFound() {
        ByteWriter writer = PlayerInfoPacket.NotFound(0x0102030405060708);
        var reader = new ByteReader(writer.Buffer, 0);
        reader.ReadShort();

        Assert.Multiple(() => {
            Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(writer.Length, Is.EqualTo(11));
        });
    }
}

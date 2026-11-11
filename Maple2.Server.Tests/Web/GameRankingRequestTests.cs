using System.IO;
using System.Text;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;

namespace Maple2.Server.Tests.Web;

// The irrq.aspx body, written the way CSoapClient::RequestGameRanking writes it.
public class GameRankingRequestTests {
    private static byte[] ClientBody(int type, int sub, int date, string search) {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(type);
        writer.Write(0);
        writer.Write(7L); // account
        writer.Write(1001L); // character
        writer.Write(0L); // searched character
        writer.Write(sub);
        writer.Write(date);
        byte[] text = Encoding.Unicode.GetBytes(search);
        writer.Write(text.Length); // the byte length (GetLength() * 2)
        writer.Write(text);
        writer.Flush();
        return stream.ToArray();
    }

    [Test]
    public void DarkStreamBoard() {
        // BuildKey, board 3, tab 1: 0x22, the job tab and the tab's season id.
        GameRankingRequest request = GameRankingRequest.Read(new ByteReader(ClientBody(0x22, 10, 285, "Abc")));
        Assert.Multiple(() => {
            Assert.That(request.Type, Is.EqualTo(GameRankingType.DarkDescentPreviousSeason));
            Assert.That(request.AccountId, Is.EqualTo(7));
            Assert.That(request.CharacterId, Is.EqualTo(1001));
            Assert.That(request.RankingId, Is.EqualTo(10));
            Assert.That(request.SeasonId, Is.EqualTo(285));
            Assert.That(request.UserName, Is.EqualTo("Abc"));
            Assert.That(request.KeyNotBuilt, Is.False);
        });
    }

    [Test]
    public void NoTab_KeyNotBuilt() {
        // A board with no tab (no client season at the server time): the key stays default.
        GameRankingRequest request = GameRankingRequest.Read(new ByteReader(ClientBody(0, 0, 0, "")));
        Assert.Multiple(() => {
            Assert.That(request.KeyNotBuilt, Is.True);
            Assert.That(request.CharacterId, Is.EqualTo(1001));
            Assert.That(request.UserName, Is.Empty);
        });
    }
}

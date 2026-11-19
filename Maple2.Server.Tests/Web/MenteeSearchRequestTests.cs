using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;

namespace Maple2.Server.Tests.Web;

// The mentee search (ruq.aspx) as the client writes it (CSoapClient::RequestMenteeSearch): user type, account,
// character, the mentoring event's id, and 2. Any event id is a search; it once had to be 5.
public class MenteeSearchRequestTests {
    private static byte[] Body(int mentoringId, int option = 2) {
        var writer = new ByteWriter();
        writer.WriteInt(1);
        writer.WriteLong(10000000001);
        writer.WriteLong(20000000001);
        writer.WriteInt(mentoringId);
        writer.WriteInt(option);
        return writer.ToArray();
    }

    [TestCase(0)]
    [TestCase(3)]
    [TestCase(5)]
    [TestCase(1001)]
    public void TheClientsRequest_IsASearchForAnyMentoringId(int mentoringId) {
        WebController.MenteeSearchRequest? request = WebController.ReadMenteeSearch(Body(mentoringId));
        Assert.That(request, Is.EqualTo(new WebController.MenteeSearchRequest(1, 10000000001, 20000000001, mentoringId, 2)));
        Assert.That(Body(mentoringId), Has.Length.EqualTo(WebController.MenteeSearchSize));
    }

    [Test]
    public void AShortRequest_IsRefused() {
        Assert.That(WebController.ReadMenteeSearch(Body(3)[..^1]), Is.Null);
        Assert.That(WebController.ReadMenteeSearch([]), Is.Null);
    }
}
